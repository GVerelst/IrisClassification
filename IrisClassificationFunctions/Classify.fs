namespace IrisClassificationFunctions

open System.IO
open Microsoft.Extensions.Configuration // Namespace for ConfigurationBuilder
open Microsoft.WindowsAzure.Storage     // Namespace for CloudStorageAccount
open Microsoft.Azure.WebJobs
open Microsoft.AspNetCore.Mvc;
open Microsoft.Azure.WebJobs.Extensions.Http;
open Microsoft.AspNetCore.Http;
open Microsoft.Extensions.Logging;

open Microsoft.ML       // Namespace for ML.NET
open Microsoft.ML.Data  // Namespace for ColumnName attribute

module Classify =

    // types
    type IrisData() =
        [<ColumnName "sepal_length"; DefaultValue>]
        val mutable public SepalLength: float32
    
        [<ColumnName "sepal_width"; DefaultValue>]
        val mutable public SepalWidth: float32
    
        [<ColumnName "petal_length"; DefaultValue>]
        val mutable public PetalLength:float32
    
        [<ColumnName "petal_width"; DefaultValue>]
        val mutable public PetalWidth:float32
    
        [<ColumnName "species"; DefaultValue>]
        val mutable public Label: string

    type Prediction() =
        [<ColumnName "PredictedLabel";DefaultValue>]
        val mutable public PredictedLabel : string

    let GetSetting name =
        let currentFolder = Directory.GetCurrentDirectory()
        let builder = (new ConfigurationBuilder()).SetBasePath(currentFolder).AddJsonFile("local.settings.json", true, true).AddEnvironmentVariables()
        let config = builder.Build()
        config.[name]

    let GetBlob folder name connection =
        let storageAccount = CloudStorageAccount.Parse(connection)
        let blobClient = storageAccount.CreateCloudBlobClient()
        let container = blobClient.GetContainerReference(folder)
        let blobRef = container.GetBlockBlobReference(name)     
        let stream = new MemoryStream()
        (blobRef.DownloadToStreamAsync stream).Wait()
        stream.Seek(0L, SeekOrigin.Begin) |> ignore
        stream

    let CreateEngine (stream: Stream) =
        let mlContext = new MLContext()

        let mutable inputSchema = Unchecked.defaultof<DataViewSchema>

        let mlModel = mlContext.Model.Load(stream, &inputSchema)
        mlContext.Model.CreatePredictionEngine<IrisData, Prediction>(mlModel)

    // no error checking here, in production code this is of course not acceptable
    // parameters that are "forgotten" or in a wrong format in the request will be 0
    let GetIrisData req =
        let GetParmAsFloat (req: HttpRequest) name =
            let p = req.Query.[name]
            if p.Count = 0 then 0.0f
            else p.ToString() |> float32

        let input = IrisData()
        input.SepalLength <- GetParmAsFloat req "sepalLength"
        input.SepalWidth <- GetParmAsFloat req "sepalWidth"
        input.PetalLength <- GetParmAsFloat req "petalLength"
        input.PetalWidth <- GetParmAsFloat req "petalWidth"
        input

    let engine = GetSetting "AzureWebJobsStorage" |> GetBlob "trainingdata" "MLModel.zip"
                                                  |> CreateEngine 

    [<FunctionName("Classify")>]
    let Run ([<HttpTrigger(AuthorizationLevel.Function, [|"get"|])>] req: HttpRequest) (log: ILogger) = 
        async {
            log.LogInformation("F# HTTP trigger Classify function processed a request.")

            let input = GetIrisData req
            let result = engine.Predict(input)

            return new OkObjectResult(result)
        }
        |> Async.StartAsTask

