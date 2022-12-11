using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using System.Linq;


namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LogController : ControllerBase
    {
        public LogController()
        {
        }

        [HttpGet("[action]")]
        public List<string> GetLog()
        {
            var rootFolder = Environment.CurrentDirectory + "/logs";
            var LogList = Directory.GetFiles(rootFolder, "*", SearchOption.AllDirectories);

            foreach (var logFile in LogList)
            {
                var logFileName = Path.GetFileNameWithoutExtension(logFile);
                var currrentDay = DateTime.Now.Day < 10 ? "0" + DateTime.Now.Day.ToString() : DateTime.Now.Day.ToString();
                if (logFileName == $"{DateTime.Now.Year}{DateTime.Now.Month}{currrentDay}")
                {
                    return GetLog(logFile);
                }
            }

            return new List<string>();

        }

        private static List<string> GetLog(string fileName)
        {
            var lines = System.IO.File.ReadAllLines(fileName);
            return lines.Reverse().ToList();
        }
    }
}

