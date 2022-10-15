using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using Microsoft.ML;
using MarginCoin.Class;
using static MarginCoin.Class.Prediction;
using Newtonsoft.Json;
using System.Linq;

namespace MarginCoin.Misc
{
    public static class AIHelper
    {

        public static List<ModelOutput> GetPrediction(List<Candle> quoteList)
        {
            List<ModelOutput> predictionList = new List<ModelOutput>();

            if (quoteList.Count == 0) return predictionList;

            //2 - List models available
            var rootFolder = Environment.CurrentDirectory + "/AI";
            var modelPathList = Directory.GetFiles(rootFolder, "*", SearchOption.AllDirectories);

            if (modelPathList.Length == 0)
                return predictionList;

            ModelInput newModelInput = new ModelInput()
            {
                Rsi = (float)quoteList.Last().Rsi,
                MacdHistN0 = (float)quoteList.Last().MacdHist,
                MacdHistN1 = (float)quoteList[quoteList.Count - 2].MacdHist,
                MacdHistN2 = (float)quoteList[quoteList.Count - 3].MacdHist,
                MacdHistN3 = (float)quoteList[quoteList.Count - 4].MacdHist,
            };

            // //3 - Iterate throw model and fire prediction
            foreach (var modelPath in modelPathList)
            {
                ModelOutput output = new ModelOutput();

                var fromIndex = Path.GetFileName(modelPath).IndexOf("-") + 1;
                var toIndex = Path.GetFileName(modelPath).Length - fromIndex - 4;
                
                output = CalculatePrediction(newModelInput, modelPath);
                output.ModelName = Path.GetFileName(modelPath).Substring(fromIndex, toIndex);
                
                predictionList.Add(output);
            }

            return predictionList;
        }

        private static ModelOutput CalculatePrediction(ModelInput data, string modelPath)
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

        private static ModelOutput PredictFuturePrice(ModelInput input, ITransformer model)
        {
            MLContext mlContext = new MLContext();
            var predEngine = mlContext.Model.CreatePredictionEngine<ModelInput, ModelOutput>(model);
            ModelOutput prediction = predEngine.Predict(new ModelInput
            {
                Rsi = (float)input.Rsi,
                MacdHistN0 = (float)input.MacdHistN0,
                MacdHistN1 = (float)input.MacdHistN1,
                MacdHistN2 = (float)input.MacdHistN2,
                MacdHistN3 = (float)input.MacdHistN3,
            });

            return prediction;
        }
    }
}