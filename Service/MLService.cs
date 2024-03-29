using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using MarginCoin.Misc;
using MarginCoin.MLClass;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace MarginCoin.Service
{
    public class MLService : IMLService
    {
        private readonly string downloadFolder;
        private readonly string screenShotFolder;
        private readonly string imageFolder;
        private readonly List<int> reccurence;
        private ILogger _logger;
        private IHubContext<SignalRHub> _hub;
        private System.Timers.Timer MLTimer = new System.Timers.Timer();
        public List<MLPrediction> MLPredList { get; set; }

        private bool isFirstTrigger = false;

        public MLService(ILogger<MLService> logger,
            IHubContext<SignalRHub> hub)
        {
            _logger = logger;
            _hub = hub;
            MLPredList = new List<MLPrediction>();

            downloadFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"/Downloads";
            screenShotFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"/Downloads/MCModel";
            imageFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"/Downloads/MCImage";
        }

        public void InitML()
        {
            isFirstTrigger = true;
            MLTimer.Interval = 60000; //every 2 min
            MLTimer.Elapsed += new ElapsedEventHandler(MLTimer_Elapsed);
            MLTimer.Start();
        }

        public void StopML()
        {
            MLTimer.Stop();
        }

        private void MLTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (!isFirstTrigger && Globals.isTradingOpen)
            {
                _logger.LogWarning($"New call at {DateTime.Now.Minute} for ExportChart UI");
                _hub.Clients.All.SendAsync("exportChart");
            }
            else
            {
                  isFirstTrigger = false;
                 _logger.LogWarning($"Initial call for ExportChart UI");
                _hub.Clients.All.SendAsync("exportChart");

                MLTimer.Stop();
                MLTimer.Interval = 900000; 
                MLTimer.Start();
            }
        }

        //Callback from UI after chart export
        public void UpdateML()
        {
            try
            {
                //read all images available
                List<string> imagePathList = Directory.GetFiles(downloadFolder, "*.jpeg", SearchOption.TopDirectoryOnly).ToList();

                foreach (var imagePath in imagePathList)
                {
                    //Backup the image Not needed anymore
                    //File.Copy(imagePath, Path.Combine(screenShotFolder, Path.GetFileNameWithoutExtension(imagePath) + DateTime.Now.Year + DateTime.Now.Month + DateTime.Now.Day + "-" +  DateTime.Now.Hour + DateTime.Now.Minute + Path.GetExtension(imagePath)), true);

                    //Crop the image and delete original
                    CropImage(imagePath, downloadFolder);

                    //Test image with ML algo and store result in a list
                    ProcessImage(imagePath);
                }
                 _logger.LogWarning($"----------------ML Updated---------------------");
            }
            catch (System.Exception e)
            {
                _logger.LogError(e, "MLService UpdateML fail");
            }
        }

        public void CleanImageFolder()
        {
            var imagePathList = Directory.GetFiles(downloadFolder, "*.jpeg", SearchOption.TopDirectoryOnly).ToList();
            foreach (string imagePath in imagePathList)
            {
                File.Delete(imagePath);
            }
        }

        private void CropImage(string filename, string downloadFolder)
        {
            //System.Drawing replaced with SixLabors.ImageSharp for IOS
            using (var img = Image.Load(filename))
            {
                var ttt = img.Width;
                var ddd = img.Height;
                Image myImage = img.Clone(x=>x.Crop(new Rectangle(img.Width-200, img.Height-400, 180, 200)));
                myImage.Save(filename);
            }
        }

        private void ProcessImage(string imagePath)
        {
            // Create single instance of sample data from first line of dataset for model input
            var imageBytes = File.ReadAllBytes(imagePath);
            var imageName = Path.GetFileNameWithoutExtension(imagePath);
            var previousPred = MLPredList.Find(p => p.Symbol == imageName);
            MCModel.ModelInput sampleData = new MCModel.ModelInput()
            {
                ImageSource = imageBytes,
            };

            // // Make a single prediction
            var predictionResult = MCModel.Predict(sampleData);
            _logger.LogWarning($"Call Prediction on {imageName} {predictionResult.PredictedLabel} {predictionResult.Score[0]}/{predictionResult.Score[1]}");

            if (previousPred != null)
            {
                MLPredList.Remove(previousPred);
            }

            MLPredList.Add(new MLPrediction
            {
                Symbol = Path.GetFileNameWithoutExtension(imagePath),
                Score = predictionResult.Score,
                PredictedLabel = predictionResult.PredictedLabel
            });

            //File.Copy(imagePath, Path.Combine(imageFolder, Path.GetFileNameWithoutExtension(imagePath) + DateTime.Now.Year + DateTime.Now.Month + DateTime.Now.Day + "-" + DateTime.Now.Hour + DateTime.Now.Minute + Path.GetExtension(imagePath)), true);
            File.Delete(imagePath);
        }
    }
}