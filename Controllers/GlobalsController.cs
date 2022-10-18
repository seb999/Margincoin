using Microsoft.AspNetCore.Mvc;


namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GlobalsController : ControllerBase
    {
        public GlobalsController()
        {
        }

        [HttpGet("[action]/{isProd}")]
        public void SetProdParameter(bool isProd)
        {
            Globals.isProd = isProd;
        }

    }
}

