using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using MarginCoin.Model;

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

         [HttpGet("[action]/{date}")]
        public List<Order> GetAllOrderFromDate(string date){
            List<Order> myOrders =  _appDbContext.Order.ToList();
            foreach (var item in myOrders)
            {
                item.OpenDate = item.OpenDate.Split(" ")[0];
            }

            return myOrders.Where(p=>DateTime.ParseExact(p.OpenDate, "dd/MM/yyyy",System.Globalization.CultureInfo.InvariantCulture).CompareTo(DateTime.Parse(date))>=0).ToList();
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
            catch (System.Exception ex)
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
            catch (System.Exception ex)
            {
                return false;
            }
            return true;
        }
    }
}
