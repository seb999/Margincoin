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

        [HttpGet("[action]")]
        public List<Order> GetAllCompletedOrder(){
            return _appDbContext.Order.Where(p=>p.IsClosed == 1).ToList();
        }

        [HttpPost("[action]")]
        public bool OpenOrder([FromBody] Order order)
        {
            try
            {
                order.OpenDate = DateTime.Now.ToString();
                order.IsClosed = 0;
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
        public bool CloseOrder(int id, decimal closePrice)
        {
             try
            {
                Order myOrder = _appDbContext.Order.Where(p=>p.Id == id).Select(p=>p).FirstOrDefault();
                myOrder.ClosePrice = closePrice;
                myOrder.IsClosed = 1;
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
