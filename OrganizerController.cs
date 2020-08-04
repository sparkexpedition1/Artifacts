using DataBench.Models;
using Microsoft.WindowsAzure.MediaServices.Client;
using Microsoft.WindowsAzure.MediaServices.Client.ContentKeyAuthorization;
using Microsoft.WindowsAzure.MediaServices.Client.DynamicEncryption;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.RetryPolicies;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Mvc;
using IAsset = Microsoft.WindowsAzure.MediaServices.Client.IAsset;
using IContentKey = Microsoft.WindowsAzure.MediaServices.Client.IContentKey;
using IJob = Microsoft.WindowsAzure.MediaServices.Client.IJob;
using IMediaProcessor = Microsoft.WindowsAzure.MediaServices.Client.IMediaProcessor;

namespace DataBench.Controllers
{
    public class OrganizerController : Controller
    {
        // GET: Organizer
        public ActionResult Organizer()
        {
            return View();
        }

        private static readonly string _mediaFiles =
             Path.GetFullPath(@"../..\Media");


        private static readonly bool useAESRestriction = false;

        public string textParsed { get; set; }

        private static readonly string _sourceStorageAccountName =
           ConfigurationManager.AppSettings["SourceStorageAccountName"];
        private static readonly string _sourceStorageAccountKey =
            ConfigurationManager.AppSettings["SourceStorageAccountKey"];
        private static readonly string _NameOfBlobContainerYouWantToCopy =
            ConfigurationManager.AppSettings["NameOfBlobContainerYouWantToCopy"];

        private static readonly string mediaServiceStorageConnectionString = ConfigurationManager.AppSettings["MediaServiceStorageConnectionString"];
        private static readonly string mediaServiceStorageContainerReference = ConfigurationManager.AppSettings["MediaServiceStorageContainerReference"];

        //// DEPRECATED method
        //private static readonly string mediaServiceAccountName = ConfigurationManager.AppSettings["MediaServiceAccountName"];
        //private static readonly string mediaServiceAccountKey = ConfigurationManager.AppSettings["MediaServiceAccountKey"];
        //private static readonly CloudMediaContext context = new CloudMediaContext(new MediaServicesCredentials(mediaServiceAccountName, mediaServiceAccountKey));

        //// Azure Active Directory (AAD)
        private static readonly string mediaServiceADTenantDomain = ConfigurationManager.AppSettings["MediaServiceADTenantDomain"];
        private static readonly string mediaServiceADRestApiEndpoint = ConfigurationManager.AppSettings["MediaServiceADRestApiEndpoint"];
        private static readonly string mediaServiceADApplicationId = ConfigurationManager.AppSettings["MediaServiceADApplicationId"];
        private static readonly string mediaServiceADApplicationSecret = ConfigurationManager.AppSettings["MediaServiceADApplicationSecret"];
        private static readonly AzureAdTokenCredentials azureAdTokenCredentials = new AzureAdTokenCredentials(mediaServiceADTenantDomain, new AzureAdClientSymmetricKey(mediaServiceADApplicationId, mediaServiceADApplicationSecret), AzureEnvironments.AzureCloudEnvironment);
       // private static readonly AzureAdTokenCredentials azureAdTokenCredentials = null;
        private static readonly AzureAdTokenProvider azureAdTokenProvider = new AzureAdTokenProvider(azureAdTokenCredentials);
        private static readonly CloudMediaContext context = new CloudMediaContext(new Uri(mediaServiceADRestApiEndpoint), azureAdTokenProvider);

        private static CloudStorageAccount _sourceStorageAccount = null;
        private static CloudStorageAccount _destinationStorageAccount = null;


        public ActionResult ViewPortal()
        {

            var model = new List<VideoModel>();
           // var dal = new Repository();
            //var videos = db.Videos.OrderByDescending(p => p.Id).ToList().Take(2);
            //var videos = dal.GetVideosList(); for sql db
            WebClient wc = new WebClient();
            var videojson = wc.DownloadString(Server.MapPath("~/Content/Video.json"));
            var videos = JsonConvert.DeserializeObject<List<Video>>(videojson);
            foreach (var video in videos)
            {
                var viewModel = new VideoModel();
                viewModel.Id = video.Id;
                viewModel.EncodedAssetId = video.EncodedAssetId;
                viewModel.IsEncrypted = video.IsEncrypted;
                viewModel.LocatorUri = video.LocatorUri;
                viewModel.Status = AzureMediaAsset.GetEncodingJobStatus(video.EncodingJobId);
                viewModel.FileType = video.filetype;
                viewModel.FileName = video.AssetFileName ?? null; //if assetfilename is null it will take null
                // If encrypted content, then get token to play
                if (video.IsEncrypted)
                {
                    IAsset asset = GetAssetById(video.EncodedAssetId);
                    viewModel.Token = AzureMediaAsset.GetTestToken(asset.Id, asset);
                }

                model.Add(viewModel);
            }

            return View(model);
        }

        internal static IAsset GetAssetById(string assetId)
        {
            IAsset theAsset = (from a in context.Assets
                               where a.Id == assetId
                               select a).FirstOrDefault();
            return theAsset;
        }

        [HttpPost]
        public ActionResult SetMetadata(int blocksCount, string fileName, long fileSize)
        {
            var container = CloudStorageAccount.Parse(mediaServiceStorageConnectionString).CreateCloudBlobClient().GetContainerReference(mediaServiceStorageContainerReference);
            var fileNameInBlob = DateTime.Now.ToString().Replace("/", "-").Replace(":", "-").Replace(" ", "_") + ".mp4";
            container.CreateIfNotExists();

            var fileToUpload = new CloudFile()
            {
                BlockCount = blocksCount,
                FileName = fileNameInBlob,
                OriginalName = fileName,
                Size = fileSize,
                BlockBlob = container.GetBlockBlobReference(fileNameInBlob),
                StartTime = DateTime.Now,
                IsUploadCompleted = false,
                UploadStatusMessage = string.Empty
            };

            Session.Add("CurrentFile", fileToUpload);

            return Json(true);
        }

        [HttpPost]
        [ValidateInput(false)]
        public ActionResult UploadChunk(int id)
        {
            HttpPostedFileBase request = Request.Files["Slice"];
            byte[] chunk = new byte[request.ContentLength];
            request.InputStream.Read(chunk, 0, Convert.ToInt32(request.ContentLength));
            JsonResult returnData = null;
            string fileSession = "CurrentFile";
            if (Session[fileSession] != null)
            {
                CloudFile model = (CloudFile)Session[fileSession];
                returnData = UploadCurrentChunk(model, chunk, id);
                if (returnData != null)
                {
                    return returnData;
                }
                if (id == model.BlockCount)
                {
                    return CommitAllChunks(model);
                }
            }
            else
            {
                returnData = Json(new
                {
                    error = true,
                    isLastBlock = false,
                    message = string.Format(CultureInfo.CurrentCulture, "Failed to Upload file.", "Session Timed out")
                });
                return returnData;
            }
            return Json(new { error = false, isLastBlock = false, message = string.Empty });
        }


        private JsonResult UploadCurrentChunk(CloudFile model, byte[] chunk, int id)
        {
            using (var chunkStream = new MemoryStream(chunk))
            {
                var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                        string.Format(CultureInfo.InvariantCulture, "{0:D4}", id)));
                try
                {
                    model.BlockBlob.PutBlock(
                        blockId,
                        chunkStream, null, null,
                        new BlobRequestOptions()
                        {
                            RetryPolicy = new LinearRetry(TimeSpan.FromSeconds(10), 3)
                        },
                        null);
                    return null;
                }
                catch (StorageException e)
                {
                    model.IsUploadCompleted = true;
                    model.UploadStatusMessage = "Failed to Upload file. Exception - " + e.Message;
                    return Json(new { error = true, isLastBlock = false, message = model.UploadStatusMessage });
                }
            }
        }

        private ActionResult CommitAllChunks(CloudFile model)
        {
            model.IsUploadCompleted = true;
            bool errorInOperation = false;
            try
            {
                var blockList = Enumerable.Range(1, (int)model.BlockCount).ToList<int>().ConvertAll(rangeElement => Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Format(CultureInfo.InvariantCulture, "{0:D4}", rangeElement))));
                model.BlockBlob.PutBlockList(blockList);
                var duration = DateTime.Now - model.StartTime;
                float fileSizeInKb = model.Size / 1024;
                string fileSizeMessage = fileSizeInKb > 1024 ? string.Concat((fileSizeInKb / 1024).ToString(CultureInfo.CurrentCulture), " MB") : string.Concat(fileSizeInKb.ToString(CultureInfo.CurrentCulture), " KB");
                model.UploadStatusMessage = string.Format(CultureInfo.CurrentCulture, "File of size {0} took {1} seconds to upload.", fileSizeMessage, duration.TotalSeconds);

                IAsset mediaServiceAsset = CreateMediaAsset(model);
                model.AssetId = mediaServiceAsset.Id;
                //IAsset file = RunIndexingJob(mediaServiceAsset.Id);
                //// Download the job output asset.
                //var restult=DownloadAsset(file, HttpContext.Server.MapPath("~/Media/Output"));

            }
            catch (StorageException e)
            {
                model.UploadStatusMessage = "Failed to upload file. Exception - " + e.Message;
                errorInOperation = true;
            }
            return Json(new
            {
                error = errorInOperation,
                isLastBlock = model.IsUploadCompleted,
                message = model.UploadStatusMessage,
                assetId = model.AssetId,
            });
        }


        private IAsset CreateMediaAsset(CloudFile model)
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(mediaServiceStorageConnectionString);
            CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer mediaBlobContainer = cloudBlobClient.GetContainerReference(mediaServiceStorageContainerReference);

            mediaBlobContainer.CreateIfNotExists();

            // Create a new asset.
            IAsset asset = context.Assets.Create("UploadedVideo-" + Guid.NewGuid().ToString().ToLower(), AssetCreationOptions.None);
            IAccessPolicy writePolicy = context.AccessPolicies.Create("writePolicy", TimeSpan.FromMinutes(120), AccessPermissions.Write);
            ILocator destinationLocator = context.Locators.CreateLocator(LocatorType.Sas, asset, writePolicy);

            // Get the asset container URI and copy blobs from mediaContainer to assetContainer.
            Uri uploadUri = new Uri(destinationLocator.Path);
            string assetContainerName = uploadUri.Segments[1];
            CloudBlobContainer assetContainer = cloudBlobClient.GetContainerReference(assetContainerName);
            string fileName = HttpUtility.UrlDecode(Path.GetFileName(model.BlockBlob.Uri.AbsoluteUri));

            var sourceCloudBlob = mediaBlobContainer.GetBlockBlobReference(fileName);
            sourceCloudBlob.FetchAttributes();

            if (sourceCloudBlob.Properties.Length > 0)
            {
                IAssetFile assetFile = asset.AssetFiles.Create(fileName);
                var destinationBlob = assetContainer.GetBlockBlobReference(fileName);

                destinationBlob.DeleteIfExists();
               // not present anymore
              //  destinationBlob.StartCopyFromBlob(sourceCloudBlob);
                destinationBlob.FetchAttributes();
                if (sourceCloudBlob.Properties.Length != destinationBlob.Properties.Length)
                    model.UploadStatusMessage += "Failed to copy as Media Asset!";
            }
            destinationLocator.Delete();
            writePolicy.Delete();
            sourceCloudBlob.Delete();  //delete temp blob

            // Refresh the asset.
            asset = context.Assets.Where(a => a.Id == asset.Id).FirstOrDefault();

            var ismAssetFiles = asset.AssetFiles.FirstOrDefault();
            ismAssetFiles.IsPrimary = true;
            ismAssetFiles.Update();

            model.UploadStatusMessage += " Media file uploaded successfully by id: " + asset.Id;
            model.AssetId = asset.Id;

            return asset;
        }

        public IAsset CreateMediaAsset()
        {
            _sourceStorageAccount =
                new CloudStorageAccount(new StorageCredentials(_sourceStorageAccountName,
                    _sourceStorageAccountKey), true);

            _destinationStorageAccount =
                new CloudStorageAccount(new StorageCredentials(_sourceStorageAccountName,
                    _sourceStorageAccountKey), true);

            CloudBlobClient sourceCloudBlobClient =
                _sourceStorageAccount.CreateCloudBlobClient();
            CloudBlobContainer sourceContainer =
                sourceCloudBlobClient.GetContainerReference(_NameOfBlobContainerYouWantToCopy);


            CloudBlobClient destBlobStorage = _destinationStorageAccount.CreateCloudBlobClient();

            // Create a new asset. 
            IAsset asset = context.Assets.Create("NewAsset_" + Guid.NewGuid(), AssetCreationOptions.None);

            IAccessPolicy writePolicy = context.AccessPolicies.Create("writePolicy",
                TimeSpan.FromHours(24), AccessPermissions.Write);

            ILocator destinationLocator =
                context.Locators.CreateLocator(LocatorType.Sas, asset, writePolicy);

            // Get the asset container URI and Blob copy from mediaContainer to assetContainer. 
            CloudBlobContainer destAssetContainer =
                destBlobStorage.GetContainerReference((new Uri(destinationLocator.Path)).Segments[1]);

            if (destAssetContainer.CreateIfNotExists())
            {
                destAssetContainer.SetPermissions(new BlobContainerPermissions
                {
                    PublicAccess = BlobContainerPublicAccessType.Blob
                });
            }

            var blobList = sourceContainer.ListBlobs();

            foreach (CloudBlockBlob sourceBlob in blobList)
            {
                //var assetFile = asset.AssetFiles.Create((sourceBlob as ICloudBlob).Name);

                //ICloudBlob destinationBlob = destAssetContainer.GetBlockBlobReference(assetFile.Name);

                //CopyBlob(sourceBlob, destAssetContainer);

                //sourceBlob.FetchAttributes();
                //assetFile.ContentFileSize = (sourceBlob as ICloudBlob).Properties.Length;
                //assetFile.Update();
                //Console.WriteLine("File {0} is of {1} size", assetFile.Name, assetFile.ContentFileSize);
            }

            asset.Update();

            destinationLocator.Delete();
            writePolicy.Delete();

            // Set the primary asset file.
            // If, for example, we copied a set of Smooth Streaming files, 
            // set the .ism file to be the primary file. 
            // If we, for example, copied an .mp4, then the mp4 would be the primary file. 
            var ismAssetFile = asset.AssetFiles.ToList().
                Where(f => f.Name.EndsWith(".ism", StringComparison.OrdinalIgnoreCase)).ToArray().FirstOrDefault();

            // The following code assigns the first .ism file as the primary file in the asset.
            // An asset should have one .ism file.  
            if (ismAssetFile != null)
            {
                ismAssetFile.IsPrimary = true;
                ismAssetFile.Update();
            }

            //CloudStorageAccount storageAccount = CloudStorageAccount.Parse(mediaServiceStorageConnectionString);
            //CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();
            //CloudBlobContainer mediaBlobContainer = cloudBlobClient.GetContainerReference(mediaServiceStorageContainerReference);

            //mediaBlobContainer.CreateIfNotExists();

            //// Create a new asset.
            //IAsset asset = context.Assets.Create("UploadedVideo-" + Guid.NewGuid().ToString().ToLower(), AssetCreationOptions.None);
            //IAccessPolicy writePolicy = context.AccessPolicies.Create("writePolicy", TimeSpan.FromMinutes(120), AccessPermissions.Write);
            //ILocator destinationLocator = context.Locators.CreateLocator(LocatorType.Sas, asset, writePolicy);

            //// Get the asset container URI and copy blobs from mediaContainer to assetContainer.
            //Uri uploadUri = new Uri(destinationLocator.Path);
            //string assetContainerName = uploadUri.Segments[1];
            //CloudBlobContainer assetContainer = cloudBlobClient.GetContainerReference(assetContainerName);
            //string fileName = HttpUtility.UrlDecode(Path.GetFileName("https://leadsstorageacc.blob.core.windows.net/sibot/SI_Operations%20%26%20Finance_NWS_Billable%20T%26E%2C%20October%2031%2C%202017.mp4"));

            //var sourceCloudBlob = mediaBlobContainer.GetBlockBlobReference(fileName);
            //sourceCloudBlob.FetchAttributes();

            //if (sourceCloudBlob.Properties.Length > 0)
            //{
            //    IAssetFile assetFile = asset.AssetFiles.Create(fileName);
            //    var destinationBlob = assetContainer.GetBlockBlobReference(fileName);

            //    destinationBlob.DeleteIfExists();
            //    destinationBlob.StartCopyFromBlob(sourceCloudBlob);
            //    destinationBlob.FetchAttributes();
            //    if (sourceCloudBlob.Properties.Length != destinationBlob.Properties.Length)
            //        model.UploadStatusMessage += "Failed to copy as Media Asset!";
            //}
            //destinationLocator.Delete();
            //writePolicy.Delete();
            //sourceCloudBlob.Delete();  //delete temp blob

            //// Refresh the asset.
            //asset = context.Assets.Where(a => a.Id == asset.Id).FirstOrDefault();

            //var ismAssetFiles = asset.AssetFiles.FirstOrDefault();
            //ismAssetFiles.IsPrimary = true;
            //ismAssetFiles.Update();

            //model.UploadStatusMessage += " Media file uploaded successfully by id: " + asset.Id;
            //model.AssetId = asset.Id;
            //IAsset file =RunIndexingJob(asset.Id);
            //// Download the job output asset.
            //DownloadAsset(file, HttpContext.Server.MapPath("~/Media/Output"));

            EncodeToAdaptiveBitrateMP4s(asset.Id);
            return asset;
        }

        static private void CopyBlob(ICloudBlob sourceBlob, CloudBlobContainer destinationContainer)
        {
            var signature = sourceBlob.GetSharedAccessSignature(new SharedAccessBlobPolicy
            {
                Permissions = SharedAccessBlobPermissions.Read,
                SharedAccessExpiryTime = DateTime.UtcNow.AddHours(24)
            });

            var destinationBlob = destinationContainer.GetBlockBlobReference(sourceBlob.Name);

            if (destinationBlob.Exists())
            {
                Debug.WriteLine(string.Format("Destination blob '{0}' already exists. Skipping.", destinationBlob.Uri));
            }
            else
            {

                // Display the size of the source blob.
                Debug.WriteLine(sourceBlob.Properties.Length);

                Debug.WriteLine(string.Format("Copy blob '{0}' to '{1}'", sourceBlob.Uri, destinationBlob.Uri));
                destinationBlob.StartCopy(new Uri(sourceBlob.Uri.AbsoluteUri + signature));

                while (true)
                {
                    // The StartCopyFromBlob is an async operation, 
                    // so we want to check if the copy operation is completed before proceeding. 
                    // To do that, we call FetchAttributes on the blob and check the CopyStatus. 
                    destinationBlob.FetchAttributes();
                    if (destinationBlob.CopyState.Status != CopyStatus.Pending)
                    {
                        break;
                    }
                    //It's still not completed. So wait for some time.
                    System.Threading.Thread.Sleep(1000);
                }

                // Display the size of the destination blob.
                Debug.WriteLine(destinationBlob.Properties.Length);

            }
        }

        public ActionResult RunIndexingJob(string assetId)
        {
            // Create an asset and upload the input media file to storage.
            IAsset asset = GetAssetById(assetId);

            // Declare a new job.
            IJob job = context.Jobs.Create("My Indexing Job");

            // Get a reference to Azure Media Indexer 2 Preview.
            string MediaProcessorName = "Azure Media Indexer 2 Preview";

            var processor = GetLatestMediaProcessorByName(MediaProcessorName);

            // Read configuration from the specified file.


            string configuration = System.IO.File.ReadAllText(HttpContext.Server.MapPath("~/config.json"));
            // Create a task with the encoding details, using a string preset.
            ITask task = job.Tasks.AddNew("My Indexing Task",
                processor,
                configuration,
                TaskOptions.None);

            // Specify the input asset to be indexed.
            task.InputAssets.Add(asset);

            // Add an output asset to contain the results of the job.
            task.OutputAssets.AddNew("My Indexing Output Asset", AssetCreationOptions.None);
            // Launch the job.
            job.Submit();
            // Check job execution and wait for job to finish.
            //Task progressJobTask = job.GetExecutionProgressTask(CancellationToken.None);

            //progressJobTask.Wait();
            Session["indexingMediaAssets"] = job.OutputMediaAssets[0];
            //return job.OutputMediaAssets[0];
            return Json(new
            {
                message = "Indexing Job Submitted",
                assetId = assetId,
                jobId = job.Id
            });
        }

        public string DownloadAsset()
        {
            IAsset asset = (IAsset)Session["indexingMediaAssets"];
            foreach (IAssetFile file in asset.AssetFiles)
            {
                file.Download(Server.MapPath("~/Media/Output/") + file.Name);
                if (file.Name.Substring(file.Name.Length - 4) == ".vtt")
                {
                    textParsed = Parseresult("~/Media/Output/" + file.Name);
                    Session["link"] = "Media/Output/" + file.Name;
                    Session["textParsed"] = textParsed;
                    break;
                }

            }
            return textParsed;
        }

        public string VirtualPath { get; set; }
        public string FileDownloadName { get; set; }

        public string Parseresult(string path)
        {
            string textvalue = "";
            string input = System.IO.File.ReadAllText(HttpContext.Server.MapPath(path));

            var pattern = @"(?m)^(\d{2}:\d{2}:\d{2}\.\d+) +--> +(\d{2}:\d{2}:\d{2}\.\d+).*[\r\n]+\s*(?s)((?:(?!\r?\n\r?\n).)*)";
            MatchCollection matches = Regex.Matches(input, pattern);
            foreach (Match match in matches)
            {
                textvalue = textvalue + match.Groups[3].Value + "\n ";
            }
            return textvalue;
        }
        [HttpPost]
        public ActionResult EncodeToAdaptiveBitrateMP4s(string assetId)
        {
            // Note: You need atleast 1 reserve streaming unit for dynamic packaging of encoded media. If you don't have that, you can't see video file playing.

            IAsset inputAsset = GetAssetById(assetId);
            string token = string.Empty;
            string uploadFileOriginalName = string.Empty;
            string fileName = string.Empty;

            ////// Without preset (say default preset), works very well
            //IJob job = context.Jobs.CreateWithSingleTask(MediaProcessorNames.AzureMediaEncoder,
            //    MediaEncoderTaskPresetStrings.H264AdaptiveBitrateMP4Set720p,
            //    inputAsset,
            //    "UploadedVideo-" + Guid.NewGuid().ToString().ToLower() + "-Adaptive-Bitrate-MP4",
            //    AssetCreationOptions.None);
            //job.Submit();
            //IAsset encodedOutputAsset = job.OutputMediaAssets[0];


            //// XML Preset
            IJob job = context.Jobs.Create(inputAsset.Name);
            if (inputAsset.AssetType.ToString() == "Video")
            {
                IMediaProcessor processor = GetLatestMediaProcessorByName("Media Encoder Standard");
                string configuration = System.IO.File.ReadAllText(HttpContext.Server.MapPath("~/MediaServicesCustomPreset.xml"));
                ITask task = job.Tasks.AddNew(inputAsset.Name + "- encoding task", processor, configuration, TaskOptions.None);
                task.InputAssets.Add(inputAsset);
                task.OutputAssets.AddNew(inputAsset.Name + "-Adaptive-Bitrate-MP4", AssetCreationOptions.None);
            }

            else if (true)
            {
                IMediaProcessor processor = GetLatestMediaProcessorByName("Media Encoder Standard");
                string configuration = System.IO.File.ReadAllText(HttpContext.Server.MapPath("~/MediaServicesCustomPreset.xml"));
                ITask task = job.Tasks.AddNew("Media Encoder Standard encoding task", processor, "Adaptive Streaming", TaskOptions.None);

                //ITask task = job.Tasks.AddNew("My encoding task", processor, configuration, TaskOptions.None);
                task.InputAssets.Add(inputAsset);
                task.OutputAssets.AddNew(inputAsset.Name + "-Adaptive-Bitrate-MP4", AssetCreationOptions.None);
            }
            job.Submit();

            //Task progressJobTask = job.GetExecutionProgressTask(CancellationToken.None);

            //progressJobTask.Wait();
            IAsset encodedAsset = job.OutputMediaAssets[0];

            // process policy & encryption
            ProcessPolicyAndEncryption(encodedAsset);

            // Get file name
            string fileSession = "CurrentFile";
            if (Session[fileSession] != null)
            {
                CloudFile model = (CloudFile)Session[fileSession];
                uploadFileOriginalName = model.OriginalName;
                fileName = model.FileName;

            }
            // Generate Streaming URL
            string smoothStreamingUri = GetStreamingOriginLocator(encodedAsset, fileName);

            // add jobid and output asset id in database
            var video = new Video();
            video.AssetId = assetId;
            video.EncodingJobId = job.Id;
            video.EncodedAssetId = encodedAsset.Id;
            video.LocatorUri = smoothStreamingUri;
            video.IsEncrypted = useAESRestriction;
            video.IndexedAssetId = ((IAsset)Session["indexingMediaAssets"]).Id;
            video.AssetFileName = uploadFileOriginalName;
            video.text = Session["textParsed"].ToString();
            var videoformatlist = new List<string> { ".mp4", ".avi", ".mkv", ".flv", ".wmv" };
            var audioformatlist = new List<string> { ".wav", ".mp3", ".aac" };
            var format = uploadFileOriginalName.Substring(uploadFileOriginalName.Length - 4);
            if (videoformatlist.Contains(format))
            {
                video.filetype = "video";
            }
            else if (audioformatlist.Contains(format))
            {
                video.filetype = "audio";
            }
            else
            {
                video.filetype = "other";
            }

          //  Repository dal = new Repository();
            //var result = dal.SaveVideo(video);
            SaveVideoListtoJson(video);
            if (useAESRestriction)
            {
                token = AzureMediaAsset.GetTestToken(encodedAsset.Id, encodedAsset);
            }

            // Remove session
            Session.Remove("CurrentFile");
            Session.Clear();
            // return success response
            return Json(new
            {
                error = false,
                message = "Congratulations! Video is uploaded and pipelined for encoding, check console log for after encoding playback details.",
                assetId = assetId,
                jobId = job.Id,
                locator = smoothStreamingUri,
                encrypted = useAESRestriction,
                token = token

            });

        }
        public void ProcessPolicyAndEncryption(IAsset asset)
        {
            IContentKey key = CreateEnvelopeTypeContentKey(asset);

            if (useAESRestriction)
            {
                // AES restriction
                AddTokenRestrictedAuthorizationPolicy(key);
            }
            else
            {
                // No restriction
                AddOpenAuthorizationPolicy(key);
            }

            // Set asset delivery policy
            CreateAssetDeliveryPolicy(asset, key);
        }

        static public void AddOpenAuthorizationPolicy(IContentKey contentKey)
        {
            // Create ContentKeyAuthorizationPolicy with Open restrictions and create authorization policy             
            IContentKeyAuthorizationPolicy policy = context.ContentKeyAuthorizationPolicies.CreateAsync("Open Authorization Policy").Result;

            List<ContentKeyAuthorizationPolicyRestriction> restrictions = new List<ContentKeyAuthorizationPolicyRestriction>();
            ContentKeyAuthorizationPolicyRestriction restriction =
                new ContentKeyAuthorizationPolicyRestriction
                {
                    Name = "HLS Open Authorization Policy",
                    KeyRestrictionType = (int)ContentKeyRestrictionType.Open,
                    Requirements = null // no requirements needed for HLS
                };

            restrictions.Add(restriction);

            IContentKeyAuthorizationPolicyOption policyOption =
                context.ContentKeyAuthorizationPolicyOptions.Create(
                "policy",
                ContentKeyDeliveryType.BaselineHttp,
                restrictions,
                "");

            policy.Options.Add(policyOption);

            // Add ContentKeyAutorizationPolicy to ContentKey
            contentKey.AuthorizationPolicyId = policy.Id;
            IContentKey updatedKey = contentKey.UpdateAsync().Result;
        }

        static public IContentKey CreateEnvelopeTypeContentKey(IAsset asset)
        {
            // Create envelope encryption content key
            Guid keyId = Guid.NewGuid();
            byte[] contentKey = GetRandomBuffer(16);

            IContentKey key = context.ContentKeys.Create(
                                    keyId,
                                    contentKey,
                                    "ContentKey",
                                    ContentKeyType.EnvelopeEncryption);

            // Associate the key with the asset.
            asset.ContentKeys.Add(key);

            return key;
        }

        static public string GetStreamingOriginLocator(IAsset asset, string filename)
        {
            // Get a reference to the streaming manifest file from the collection of files in the asset. 
            //var assetFile = asset.AssetFiles.Where(f => f.Name.ToLower().EndsWith(".ism")).FirstOrDefault();

            // A locator expiry can be set to maximum 100 years, using 99 years below.
            TimeSpan daysForWhichStreamingUrlIsActive = DateTime.Now.AddYears(99) - DateTime.Now;
            IAccessPolicy policy = context.AccessPolicies.Create("Streaming policy", daysForWhichStreamingUrlIsActive, AccessPermissions.Read);

            // Create a locator to the streaming content on an origin.
            ILocator originLocator = context.Locators.CreateLocator(LocatorType.OnDemandOrigin, asset, policy, DateTime.UtcNow.AddMinutes(-5));

            // Create a URL to the manifest file.

            return GetWithoutHttp(originLocator.Path + Path.GetFileNameWithoutExtension(filename) + ".ism/manifest");
        }

        static public void CreateAssetDeliveryPolicy(IAsset asset, IContentKey key)
        {
            Uri keyAcquisitionUri = key.GetKeyDeliveryUrl(ContentKeyDeliveryType.BaselineHttp);

            string envelopeEncryptionIV = Convert.ToBase64String(GetRandomBuffer(16));

            // The following policy configuration specifies: key url that will have KID=<Guid> appended to the envelope and
            // the Initialization Vector (IV) to use for the envelope encryption.
            Dictionary<AssetDeliveryPolicyConfigurationKey, string> assetDeliveryPolicyConfiguration =
                new Dictionary<AssetDeliveryPolicyConfigurationKey, string>
            {
                        {AssetDeliveryPolicyConfigurationKey.EnvelopeKeyAcquisitionUrl, keyAcquisitionUri.ToString()}
            };

            IAssetDeliveryPolicy assetDeliveryPolicy =
                context.AssetDeliveryPolicies.Create(
                            "AssetDeliveryPolicy",
                            AssetDeliveryPolicyType.DynamicEnvelopeEncryption,
                            AssetDeliveryProtocol.SmoothStreaming | AssetDeliveryProtocol.HLS | AssetDeliveryProtocol.Dash,
                            assetDeliveryPolicyConfiguration);

            // Add AssetDelivery Policy to the asset
            asset.DeliveryPolicies.Add(assetDeliveryPolicy);
        }

        private static IMediaProcessor GetLatestMediaProcessorByName(string mediaProcessorName)
        {
            var processor = context.MediaProcessors.Where(p => p.Name == mediaProcessorName).
            ToList().OrderBy(p => new Version(p.Version)).LastOrDefault();

            if (processor == null)
                throw new ArgumentException(string.Format("Unknown media processor", mediaProcessorName));

            return processor;
        }

        public static string AddTokenRestrictedAuthorizationPolicy(IContentKey contentKey)
        {
            string tokenTemplateString = GenerateTokenRequirements();

            IContentKeyAuthorizationPolicy policy = context.
                                    ContentKeyAuthorizationPolicies.
                                    CreateAsync("HLS token restricted authorization policy").Result;

            List<ContentKeyAuthorizationPolicyRestriction> restrictions = new List<ContentKeyAuthorizationPolicyRestriction>();

            ContentKeyAuthorizationPolicyRestriction restriction =
                    new ContentKeyAuthorizationPolicyRestriction
                    {
                        Name = "Token Authorization Policy",
                        KeyRestrictionType = (int)ContentKeyRestrictionType.TokenRestricted,
                        Requirements = tokenTemplateString
                    };

            restrictions.Add(restriction);

            //You could have multiple options 
            IContentKeyAuthorizationPolicyOption policyOption =
                context.ContentKeyAuthorizationPolicyOptions.Create(
                    "Token option for HLS",
                    ContentKeyDeliveryType.BaselineHttp,
                    restrictions,
                    null  // no key delivery data is needed for HLS
                    );

            policy.Options.Add(policyOption);

            // Add ContentKeyAutorizationPolicy to ContentKey
            contentKey.AuthorizationPolicyId = policy.Id;
            IContentKey updatedKey = contentKey.UpdateAsync().Result;

            return tokenTemplateString;
        }

        static private string GenerateTokenRequirements()
        {
            TokenRestrictionTemplate template = new TokenRestrictionTemplate(TokenType.SWT);

            template.PrimaryVerificationKey = new SymmetricVerificationKey();
            template.AlternateVerificationKeys.Add(new SymmetricVerificationKey());
            template.Audience = ConfigurationManager.AppSettings["Audience"];
            template.Issuer = ConfigurationManager.AppSettings["Issuer"];

            template.RequiredClaims.Add(TokenClaim.ContentKeyIdentifierClaim);

            return TokenRestrictionTemplateSerializer.Serialize(template);
        }

        static private byte[] GetRandomBuffer(int size)
        {
            byte[] randomBytes = new byte[size];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(randomBytes);
            }

            return randomBytes;
        }

        private static string GetWithoutHttp(string smoothStreamingUri)
        {
            if (smoothStreamingUri.StartsWith("http:"))
            {
                smoothStreamingUri = smoothStreamingUri.Substring("http:".Length);
            }

            if (smoothStreamingUri.StartsWith("https:"))
            {
                smoothStreamingUri = smoothStreamingUri.Substring("https:".Length);
            }

            return smoothStreamingUri;
        }

        // Create locator and get urls
        static public string PublishAssetGetURLs(IAsset asset)
        {
            // A locator expiry can be set to maximum 100 years, using 99 years below.
            TimeSpan daysForWhichStreamingUrlIsActive = DateTime.Now.AddYears(99) - DateTime.Now;
            IAccessPolicy videoWatchPolicy = context.AccessPolicies.Create("videoWatchPolicy", daysForWhichStreamingUrlIsActive, AccessPermissions.Read);
            ILocator destinationLocator = context.Locators.CreateLocator(LocatorType.OnDemandOrigin, asset, videoWatchPolicy);

            // Get the asset container URI
            Uri uploadUri = new Uri(destinationLocator.Path);

            // Note: You need atleast 1 reserve streaming unit for dynamic packaging of encoded media. If you don't have that, you can't see video file playing
            return uploadUri.ToString() + asset.Name + ".ism/manifest";
        }


        [HttpGet]
        public JsonResult GetEncodingJobStatus(string jobId)
        {
            StringBuilder builder = new StringBuilder();
            IJob job = GetJob(jobId);

            Debug.WriteLine("Job ID :" + job.Id);
            Debug.WriteLine("Job Name :" + job.Name);
            Debug.WriteLine("Job State :" + job.State.ToString());
            Debug.WriteLine("Job Start Time :" + job.StartTime.ToString());

            if (job.State == JobState.Error)
            {
                builder.Append("Error Details: \n");
                foreach (ITask task in job.Tasks)
                {
                    foreach (ErrorDetail detail in task.ErrorDetails)
                    {
                        builder.AppendLine("Task Id: " + task.Id);
                        builder.AppendLine("Error Code: " + detail.Code);
                        builder.AppendLine("Error Message: " + detail.Message + "\n");
                    }
                }
                Debug.WriteLine(builder);

                return Json(new { error = true, message = "Encoding error from media service" }, JsonRequestBehavior.AllowGet);
            }

            return Json(new { error = false, state = job.State.ToString(), progress = job.GetOverallProgress().ToString() }, JsonRequestBehavior.AllowGet);
        }

        static IJob GetJob(string jobId)
        {
            var jobInstance =
                from j in context.Jobs
                where j.Id == jobId
                select j;
            IJob job = jobInstance.FirstOrDefault();
            return job;
        }

        public ActionResult Delete(int? id)
        {
            return Json(new { Reponse = "Need to implement" });
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        //public ActionResult DeleteConfirmed(int id)
        //{
        //    var video = db.Videos.Find(id);
        //    db.Videos.Remove(video);
        //    db.SaveChanges();

        //    // Delete from Media Service (Asset as well as storage)
        //    DeleteMediaService(video);

        //    return RedirectToAction("Index");
        //}

        //private void DeleteMediaService(Models.v video)
        //{
        //    string assetId = video.EncodedAssetId;
        //    IAsset asset = GetAssetFromDatabase(assetId);

        //    // Now delete the asset
        //    asset.Delete();
        //}

        private IAsset GetAssetFromDatabase(string assetId)
        {
            return context.Assets.Where(a => a.Id == assetId).FirstOrDefault();
        }

        public List<string> DeleteAssetInMediaService(string assetId, string jobname)
        {
           // var dal = new Repository();
            //var videos = db.Videos.OrderByDescending(p => p.Id).ToList().Take(2);
            //List<string> videos = dal.GetVideosList().Select(p => p.AssetId).ToList();
            //List<string>list=dal.GetVideosList().Select(p => p.EncodedAssetId).ToList();
            //videos.AddRange(list);
            List<IAsset> assets = context.Assets.ToList();

            //List<IAsset> assets = new List<IAsset>();
            //List<string> selectedlist = assets.Select(p=>p.Id).Except(videos).ToList();

            List<string> selectedlist1 = assets.Select(p => p.Id).ToList();

            List<string> notfoundlist = new List<string>();

            IAsset asset = assets.Where(p => p.Id == assetId).FirstOrDefault();

            string iteratevariable = "";
            try
            {
                if (null != assets)
                {
                    int i = 0;
                    foreach (var va in assets.Select(p => p.Id).ToList())
                    {

                        var locators = context.Locators.Where(l => l.AssetId == va);
                        foreach (var l in locators)
                        {
                            l.Delete();
                        }
                        assets[i].Delete();
                        i++;
                    }


                    return new List<string>(new string[] { "Done" });
                }
                return notfoundlist;
            }
            catch (Exception ex)
            {
                if (ex.InnerException.Message.ToString().Contains("Asset's storage container cannot be found") || ex.InnerException.ToString().Contains("Asset's storage container cannot be found") || ex.Message.ToString().Contains("Asset's storage container cannot be found"))
                {
                    //selectedlist.Remove(iteratevariable);
                    notfoundlist.Add(iteratevariable);
                    DeleteAssetInMediaService(assetId, jobname);
                }

                return new List<string>(new string[] { "Failed" + ex.Message + "\n" + ex.InnerException + "\n" });
            }

        }

        public JsonResult GetAssetlist()
        {
            List<IAsset> assets = context.Assets.ToList();
            List<string> assetids = assets.Select(p => p.Id).ToList();
            int length = assets.Count();

            return Json(new { count = length, list = assetids }, JsonRequestBehavior.AllowGet);
        }

        //protected override void Dispose(bool disposing)
        //{
        //    if (disposing)
        //    {
        //        db.Dispose();
        //    }
        //    base.Dispose(disposing);
        //}

        public bool SaveVideoListtoJson(Video video)
        {
            try
            {
                WebClient wc = new WebClient();
                var videojson = wc.DownloadString(Server.MapPath("~/Content/Video.json"));
                List<Video> list = JsonConvert.DeserializeObject<List<Video>>(videojson);
                if (list.Count == 0)
                {
                    video.Id = 1;
                }
                else
                {
                    video.Id = list.Select(x => x.Id).Max() + 1;
                }
                list.Add(video);
                System.IO.File.WriteAllText(Server.MapPath("~/Content/Video.json"), JsonConvert.SerializeObject(list));
                return true;
            }
            catch
            {
                return false;
            }
        }

    }
}