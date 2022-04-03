using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using Microsoft.ML;
using MarginCoin.Class;
using static MarginCoin.Class.Prediction;
using MarginCoin.Misc;
using System.Text.Json;
using Newtonsoft.Json;
using System.Linq;

namespace MarginCoin.Controllers
{
    [Route("api/[controller]")]
    public class AIController : Controller
    {

        [HttpPost]
        [Route("/api/[controller]/GetPrediction")]
        public List<PredictionTransfer> GetPrediction([FromBody]System.Text.Json.JsonElement data)
        {
             List<PredictionTransfer> predictionList = new List<PredictionTransfer>();
             List<Candle> quoteList = JsonConvert.DeserializeObject<List<Candle>>(data.ToString());
       

            if(quoteList.Count==0) return predictionList;

            //1 - Add RSI and MACD
           TradeIndicator.CalculateIndicator(ref quoteList);

            //2 - List models available
            var rootFolder = Environment.CurrentDirectory + "/AI/";
            var modelPathList = Directory.GetFiles(rootFolder, quoteList.Last().s + "*", SearchOption.AllDirectories);

            if (modelPathList.Length == 0)
                return predictionList;

            ModelInput newModelInput = new ModelInput(){
                v = (float)quoteList.Last().v/480,  //we divide 24h volume by 480 to get 3min volumn
                Rsi = (float)quoteList.Last().Rsi,
                MacdHist = (float)quoteList.Last().MacdHist,
            };

            // //3 - Iterate throw model and fire prediction
            foreach (var modelPath in modelPathList)
            {
                PredictionTransfer prediction = new PredictionTransfer();

                var fromIndex = Path.GetFileName(modelPath).IndexOf("-") + 1;
                var toIndex = Path.GetFileName(modelPath).Length - fromIndex - 4;
                prediction.ModelName = Path.GetFileName(modelPath).Substring(fromIndex, toIndex);

                prediction.FuturePrice = CalculatePrediction(newModelInput, modelPath).FuturePrice;
                prediction.Rsi = newModelInput.Rsi;
                prediction.MacdHist = newModelInput.MacdHist;
                prediction.v = newModelInput.v;
                predictionList.Add(prediction);
            }

            return predictionList;
        }

        private ModelOutput CalculatePrediction(ModelInput data, string modelPath)
        {
            //Load model
            ITransformer loadedModel = LoadModel(modelPath);

            //Predict future price
            return PredictFuturePrice(data, loadedModel);
        }

        private static ITransformer LoadModel(string modelPath)
        {
            MLContext mlContext = new MLContext();
            DataViewSchema modelSchema;

            ITransformer loadedModel;
            using (var stream = new FileStream(modelPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                loadedModel = mlContext.Model.Load(stream, out modelSchema);
            }
            return loadedModel;
        }

        private static ModelOutput PredictFuturePrice(ModelInput intraday, ITransformer model)
        {
            MLContext mlContext = new MLContext();
            var predictionFunction = mlContext.Model.CreatePredictionEngine<ModelInput, ModelOutput>(model);
            ModelOutput prediction = predictionFunction.Predict(new ModelInput
            {
                Rsi = (float)intraday.Rsi,
                MacdHist = (float)intraday.MacdHist,
                v = (float)intraday.v,
            });

            return prediction;
        }
    }
}