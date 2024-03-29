using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using MarginCoin.Model;
using System.Globalization;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrderController : ControllerBase
    {
        private readonly ApplicationDbContext _appDbContext;

        public OrderController([FromServices] ApplicationDbContext appDbContext)
        {
            _appDbContext = appDbContext;
        }
        
        [HttpGet("[action]/{symbol}")]
        public List<Order> GetOpenOrder(string symbol){
            return _appDbContext.Order.Where(p=>p.Symbol == symbol && p.IsClosed != 1).ToList();
        }

        [HttpGet("[action]/{id}")]
        public Order GetOrder(string id){
            return _appDbContext.Order.Where(p=>p.Id == int.Parse(id)).Select(p=>p).FirstOrDefault();
        }

        [HttpGet("[action]")]
        public List<Order> GetAllCompletedOrder(){
            //return _appDbContext.Order.Where(p=>p.IsClosed == 1).ToList();
               return _appDbContext.Order.ToList();
        }

        [HttpGet("[action]")]
        public List<Order> GetPendingdOrder(){
            return _appDbContext.Order.Where(p=>p.Status == "Pending").ToList();
        }

         [HttpGet("[action]/{date}")]
        public List<Order> GetAllOrderFromDate(string date){
            var dateArray = date.Split("-");
            var myDay = dateArray[0].Length<2 ? "0" + dateArray[0] : dateArray[0];
            var myMonth = dateArray[1].Length<2 ? "0" + dateArray[1] : dateArray[1];
            var myYear = dateArray[2].Length<2 ? "0" + dateArray[2] : dateArray[2];
            
            var provider = new CultureInfo("fr-FR");
            var format = "dd/MM/yyyy";
            DateTime myDate = DateTime.ParseExact(myDay + "/" + myMonth + "/" + myYear, format, provider);

            List<Order> myOrders =  _appDbContext.Order.ToList();
            myOrders = myOrders.Where(p=>DateTime.ParseExact(p.OpenDate.Split(" ")[0], "dd/MM/yyyy",System.Globalization.CultureInfo.InvariantCulture).CompareTo(myDate)>=0).ToList();

             foreach (var item in myOrders)
            {
                item.OpenDate = item.OpenDate.Split(" ")[1];
                item.CloseDate = item.CloseDate != null ? item.CloseDate.Split(" ")[1] : null;
            }

            return myOrders;
        }

        [HttpPost("[action]")]
        public bool OpenOrder([FromBody] Order order)
        {
            try
            {
                order.OpenDate = DateTime.Now.ToString();
                order.IsClosed = 0;
                order.TakeProfit = order.OpenPrice* 1.005;
                order.Fee = Math.Round((order.OpenPrice * order.Quantity) / 100) * 0.1;
                _appDbContext.Order.Add(order);
                _appDbContext.SaveChanges(); 
            }
            catch (System.Exception)
            {
                return false;
            }
           
            return true;
        }

        [HttpGet("[action]/{id}/{closePrice}")]
        public bool CloseOrder(int id, double closePrice)
        {
             try
            {
                Order myOrder = _appDbContext.Order.Where(p=>p.Id == id).Select(p=>p).FirstOrDefault();
                myOrder.ClosePrice = closePrice;
                myOrder.Fee = myOrder.Fee + Math.Round((closePrice * myOrder.Quantity) /100) * 0.1;
                myOrder.IsClosed = 1;
                myOrder.Profit = Math.Round((closePrice - myOrder.OpenPrice) * myOrder.Quantity);
                myOrder.CloseDate = DateTime.Now.ToString();
                _appDbContext.SaveChanges(); 
            }
            catch (System.Exception)
            {
                return false;
            }
            return true;
        }
    }
}
