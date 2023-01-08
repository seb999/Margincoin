using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Timers;
using MarginCoin.Misc;
using MarginCoin.MLClass;
using MarginCoin.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace MarginCoin.Service
{
    public class MLService : IMLService
    {
        private readonly string downloadFolder;
        private readonly string screenShotFolder;
        private readonly string imageFolder;
        private ILogger _logger;
        private IHubContext<SignalRHub> _hub;
        private System.Timers.Timer MLTimer = new System.Timers.Timer();
        public List<MLPrediction> MLPredList { get; set; }

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

        public void ActivateML()
        {
            MLTimer.Interval = 60000; //every min
            MLTimer.Elapsed += new ElapsedEventHandler(MLTimer_Elapsed);
            MLTimer.Start();
        }

        public void StopML()
        {
            MLTimer.Stop();
        }

        private void MLTimer_Elapsed(object sender, ElapsedEventArgs e)
        {

            //for debug 
            // _hub.Clients.All.SendAsync("exportChart");

            //Export charts
            if (Globals.isTradingOpen
                && (DateTime.Now.Minute == 0
                || DateTime.Now.Minute == 15
                || DateTime.Now.Minute == 30
                || DateTime.Now.Minute == 45))
            {
                //CleanImageFolder();
                _hub.Clients.All.SendAsync("exportChart");
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
                    //Backup the image
                    //File.Copy(imagePath, Path.Combine(screenShotFolder, Path.GetFileNameWithoutExtension(imagePath) + DateTime.Now.Year + DateTime.Now.Month + DateTime.Now.Day + "-" +  DateTime.Now.Hour + DateTime.Now.Minute + Path.GetExtension(imagePath)), true);

                    //Crop the image and delete original
                    CropImage(imagePath, downloadFolder);

                    //Test image with ML algo and store result in a list
                    ProcessImage(imagePath);
                }
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
            var myImage = Image.FromFile(filename);
            var myBitmap = new Bitmap(myImage).Clone(new Rectangle(myImage.Width-195 ,myImage.Height-410, 180, 160), myImage.PixelFormat);

            myImage.Dispose();
            if (File.Exists(filename)) File.Delete(filename);

            myBitmap.Save(filename);
            myBitmap.Dispose();
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
            _logger.LogWarning($"Call Prediction on {imageName} {predictionResult.PredictedLabel}");

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

            File.Copy(imagePath, Path.Combine(imageFolder, Path.GetFileNameWithoutExtension(imagePath) + DateTime.Now.Year + DateTime.Now.Month + DateTime.Now.Day + "-" +  DateTime.Now.Hour + DateTime.Now.Minute + Path.GetExtension(imagePath)), true);
            File.Delete(imagePath);
        }
    }
}