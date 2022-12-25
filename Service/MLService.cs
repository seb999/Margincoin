using System;
using System.Collections.Generic;
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
        private ILogger _logger;
        private IHubContext<SignalRHub> _hub;
        private System.Timers.Timer MLTimer = new System.Timers.Timer();
        public List<MLPrediction> MLPredList {get; set;}

        public MLService(ILogger<MLService> logger,
            IHubContext<SignalRHub> hub)
        {
            _logger = logger;
            _hub = hub;
            MLPredList = new List<MLPrediction>();
        }

        public void ActivateML()
        {
            MLTimer.Interval =  60000; //every min
            MLTimer.Elapsed += new ElapsedEventHandler(MLTimer_Elapsed);
            MLTimer.Start();   
        }

        public void MLTimer_Elapsed(object sender, ElapsedEventArgs e)
        {

            //for debug 
             _hub.Clients.All.SendAsync("exportChart");

            //Export charts
            // if (DateTime.Now.Minute == 9
            //     || DateTime.Now.Minute == 12
            //     || DateTime.Now.Minute == 30
            //     || DateTime.Now.Minute == 44)
            // {
            //     _hub.Clients.All.SendAsync("exportChart");
            // }
        }

        //Callback from UI after chart export
        public void GetUpdatedML()
        {
            var downloadFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads";
            var imagePathList = Directory.GetFiles(downloadFolder, "*.jpeg", SearchOption.TopDirectoryOnly).ToList();

            //we keep a copy of all chart to train the model 
            var backupFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads\MCModel";
            foreach (var fileName in imagePathList)
            {
                 File.Copy(fileName, Path.Combine(backupFolder, Path.GetFileNameWithoutExtension(fileName)+DateTime.Now.Year + DateTime.Now.Year + DateTime.Now.Year + Path.GetExtension(fileName)), true);
            }
            
            if (imagePathList.Count == 0) return;

            foreach (string imagePath in imagePathList)
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
                _logger.LogWarning($"Call Prediction on {imageName} {predictionResult.PredictedLabel} {predictionResult.Score[0]},{predictionResult.Score[1]}");

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

                File.Delete(imagePath);
            }
        }

        public void CleanMLImageFolder()
        {
            var downloadFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads";
            var imagePathList = Directory.GetFiles(downloadFolder, "*.jpeg", SearchOption.TopDirectoryOnly).ToList();
            foreach (string imagePath in imagePathList)
            {
                File.Delete(imagePath);
            }
        }
    }
}