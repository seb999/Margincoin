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
        private readonly ApplicationDbContext _appDbContext;

        public List<MLPrediction> MLPredList {get; set;}

        private System.Timers.Timer MLTimer = new System.Timers.Timer();

        public MLService(ILogger<MLService> logger,
            [FromServices] ApplicationDbContext appDbContext,
            IHubContext<SignalRHub> hub)
        {
            _logger = logger;
            _appDbContext = appDbContext;
            _hub = hub;

            Console.WriteLine("Constructor Service ML");
        }

        public void ActivateML()
        {
            MLPredList = new List<MLPrediction>();
            MLTimer.Interval =  60000; //every min
            MLTimer.Elapsed += new ElapsedEventHandler(MLTimer_Elapsed);
            MLTimer.Start();   
        }

        public void MLTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            //Export charts
            if (DateTime.Now.Minute == 2
                || DateTime.Now.Minute == 10
                || DateTime.Now.Minute == 30
                || DateTime.Now.Minute == 59)
            {
                _hub.Clients.All.SendAsync("exportChart");
            }
        }

        //Callback from UI after chart export
        public void UpdateML()
        {
            var downloadFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads";
            var imagePathList = Directory.GetFiles(downloadFolder, "*.jpeg", SearchOption.TopDirectoryOnly).ToList();
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