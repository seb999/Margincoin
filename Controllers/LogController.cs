using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MarginCoin.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LogController : ControllerBase
    {
        private ILogger _logger;

        public LogController(
            ILogger<LogController> logger)
        {
            _logger = logger;
        }

        [HttpGet("[action]")]
        public List<string> GetLog()
        {
            try
            {
                var rootFolder = Environment.CurrentDirectory + "/logs";
                var LogList = Directory.GetFiles(rootFolder, "*", SearchOption.AllDirectories);

                //Find the log.txt of the day from all logs files
                foreach (var logFile in LogList)
                {
                    var logFileName = Path.GetFileNameWithoutExtension(logFile);
                    var currrentDay = DateTime.Now.Day < 10 ? "0" + DateTime.Now.Day.ToString() : DateTime.Now.Day.ToString();
                    if (logFileName == $"{DateTime.Now.Year}{DateTime.Now.Month}{currrentDay}")
                    {
                        //Extract the content in a list
                        return GetLog(logFile);
                    }
                }
                return new List<string>();
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, " Read the log file ");
                return new List<string>();
            }
        }

        private static List<string> GetLog(string fileName)
        {
            using (FileStream fs = System.IO.File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] b = new byte[1024];
                UTF8Encoding temp = new UTF8Encoding(true);
                int readLen;
                string rawString="";
                while ((readLen = fs.Read(b, 0, b.Length)) > 0)
                {
                    rawString = temp.GetString(b, 0, readLen);
                }

                return rawString.Split("\n").Reverse().Skip(1).ToList();
            }
        }
    }
}