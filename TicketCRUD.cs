using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.RetryPolicies;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;
using System.Text;
using System.Net;
using System.Configuration;
using System.Data.SqlClient;

namespace DataBench.Controllers
{
    public class BlobController : Controller
    {
        private static readonly string Asra = ConfigurationManager.AppSettings["Asra"];
        private static readonly string RaviKotuwar = ConfigurationManager.AppSettings["RaviKoturwar"];
        private static readonly string Joseph = ConfigurationManager.AppSettings["Joseph"];
        private static readonly string Default = ConfigurationManager.AppSettings["RaviKoturwarCotainer"];
        private static readonly string JosephContainer = ConfigurationManager.AppSettings["JosephContainer"];


        public ActionResult StickyNotes()
        {
            using (WebClient wc = new WebClient())
            {
                wc.DownloadFile("https://smdwstorage.blob.core.windows.net/donivanq/StickyNotes.json", Server.MapPath(@"~\Content\StickyNotes.json"));
            }
            return View();
        }
        public ActionResult Snapshots()
        {
            return View();
        }
        public ActionResult Upload()
        {
            //List<string> accountlist=new List<string>();
            //CloudStorageAccount storageAccount = CloudStorageAccount.Parse(miscStorageConnectionString);
            //CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();
            //foreach (CloudBlobContainer item in cloudBlobClient.ListContainers())
            //{
            //    accountlist.Add(item.Name);
            //}
            //ViewBag.Accounts = accountlist;
            return View();
        }
        public ActionResult Test()
        {
            return View();
        }
        public ActionResult Download()
        {
            return View();
        }
        public ActionResult GetBlobs(int? Id, int? page, int? limit, string sortBy, string direction, CloudFile obj)
        {
            string searchStorageConnectionString = ""; string container = "";
            try
            {
                if (obj.StorageAccount == "Asra Account")
                {
                    searchStorageConnectionString = Asra;
                    container = Default;
                }
                else if (obj.StorageAccount == "Joseph Account")
                {
                    searchStorageConnectionString = Joseph;
                    container = JosephContainer;
                }
                else
                {
                    searchStorageConnectionString = RaviKotuwar;
                    container = Default;
                }
                var records = new List<CloudFile>();
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(searchStorageConnectionString);
                CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();
                CloudBlobContainer searchBlobContainer = cloudBlobClient.GetContainerReference(container);

                //searchBlobContainer.SetPermissionsAsync(new BlobContainerPermissions
                //{
                //    PublicAccess = BlobContainerPublicAccessType.Blob
                //});
                // Get list of all blobs URLs which are stored inside container
                //IEnumerable<IListBlobItem> blobs = searchBlobContainer.ListBlobs().OrderByDescending().ToList();
                var blobs = searchBlobContainer.ListBlobs().OfType<CloudBlockBlob>().OrderByDescending(m => m.Properties.LastModified).ToList();
                var query = blobs.Select(p => new CloudFile
                {

                    FileName = HttpUtility.UrlDecode(p.Uri.AbsoluteUri.ToString().Substring(p.Uri.AbsoluteUri.ToString().LastIndexOf("/") + 1)),
                    URL = p.Uri.AbsoluteUri,
                    StartTime = p.Properties.LastModified.Value.UtcDateTime

                });

                if (!string.IsNullOrWhiteSpace(obj.FileName))
                {
                    query = query.Where(q => q.FileName.ToLower().Contains(obj.FileName.ToLower()));
                }
                var total = query.Count();
                if (page.HasValue && limit.HasValue)
                {
                    int start = (page.Value - 1) * limit.Value;
                    records = query.Skip(start).Take(limit.Value).ToList();
                }
                else
                {
                    records = query.ToList();
                }
                return this.Json(new { records, total }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return null;
            }
        }
        public async Task<ActionResult> EditBlobItem(CloudFile file)
        {
            string searchStorageConnectionString = ""; string container = "";
            try
            {
                if (file.StorageAccount == "Asra Account")
                {
                    searchStorageConnectionString = Asra;
                    container = Default;
                }
                else if (file.StorageAccount == "Joseph Account")
                {
                    searchStorageConnectionString = Joseph;
                    container = JosephContainer;
                }
                else
                {
                    searchStorageConnectionString = RaviKotuwar;
                    container = Default;
                }
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(searchStorageConnectionString);
                CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();
                CloudBlobContainer searchBlobContainer = cloudBlobClient.GetContainerReference(container);
                var oldname = HttpUtility.UrlDecode(file.URL.Substring(file.URL.LastIndexOf("/") + 1));
                CloudBlockBlob blobCopy = searchBlobContainer.GetBlockBlobReference(file.FileName);
                if (!await blobCopy.ExistsAsync())
                {
                    CloudBlockBlob blob = searchBlobContainer.GetBlockBlobReference(oldname);

                    if (await blob.ExistsAsync())
                    {
                        await blobCopy.StartCopyAsync(blob);
                        await blob.DeleteIfExistsAsync();
                        return Json(new { message = oldname + " changed to " + file.FileName });
                    }
                }
                return Json(new { message = "Failed to edit " + file.FileName });
            }
            catch (Exception)
            {
                return Json(new { message = "Failed to edit " + file.FileName });
            }
        }
        
        //Crud Operations on ticket T1804733
        public ActionResult DeleteBlobItem(CloudFile file)
        {
            string searchStorageConnectionString = "", container = "";
            try
            {
                if (file.StorageAccount == "Asra Account")
                {
                    searchStorageConnectionString = Asra;
                    container = Default;
                }
                else if (file.StorageAccount == "Joseph Account")
                {
                    searchStorageConnectionString = Joseph;
                    container = JosephContainer;
                }
                else
                {
                    searchStorageConnectionString = RaviKotuwar;
                    container = Default;
                }

                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(searchStorageConnectionString);
                CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();
                CloudBlobContainer searchBlobContainer = cloudBlobClient.GetContainerReference(container);

                CloudBlockBlob _blockBlob = searchBlobContainer.GetBlockBlobReference(file.FileName);
                //delete blob from container    
                _blockBlob.Delete();
                return Json(new { message = file.FileName + " is deleted" });
            }
            catch (Exception)
            {
                return Json(new { message = "Failed to delete " + file.FileName });
            }
        }
        [HttpPost]
        public ActionResult SetMetadata(CloudFile file)
        {
            string searchStorageConnectionString = "", refcontainer = "";
            try
            {
                if (file.StorageAccount == "Asra Account")
                {
                    searchStorageConnectionString = Asra;
                    refcontainer = Default;
                }
                else if (file.StorageAccount == "Joseph Account")
                {
                    searchStorageConnectionString = Joseph;
                    refcontainer = JosephContainer;
                }
                else
                {
                    searchStorageConnectionString = RaviKotuwar;
                    refcontainer = Default;
                }
                var container = CloudStorageAccount.Parse(searchStorageConnectionString).CreateCloudBlobClient().GetContainerReference(refcontainer);
                container.CreateIfNotExists();
                file.FileName = file.OriginalName.Replace("%2C", "");
                file.BlockBlob = container.GetBlockBlobReference(file.FileName);
                file.StartTime = DateTime.Now;
                file.IsUploadCompleted = false;
                file.UploadStatusMessage = string.Empty;

                Session.Add("CurrentFile", file);
                return Json(true);
            }
            catch (Exception ex)
            {
                return Json(false);
            }
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
                message = model.UploadStatusMessage
            });
        }


        public ActionResult GetStickyNotes(string owner)
        {
            string path = Server.MapPath(@"~\Content\StickyNotes.json");
            List<Sticky> stickylist = new List<Sticky>();
            using (WebClient wc = new WebClient())
            {
                var json = wc.DownloadString(path);
                stickylist = JsonConvert.DeserializeObject<List<Sticky>>(json);
            }
            stickylist = stickylist.Where(x => x.Owner == owner).ToList();

            return this.Json(stickylist, JsonRequestBehavior.AllowGet);
        }
        public ActionResult SaveStickyNotes(List<Sticky> records)
        {
            string path = Server.MapPath(@"~\Content\StickyNotes.json");
            List<Sticky> stickylist = new List<Sticky>();
            using (WebClient wc = new WebClient())
            {
                var json = wc.DownloadString(path);
                stickylist = JsonConvert.DeserializeObject<List<Sticky>>(json);
            }
            int maxid = stickylist.Select(x => x.StickyId).Max();
            foreach (Sticky item in records)
            {
                item.StickyId = ++maxid;
                stickylist.Add(item);
            }
            string output = JsonConvert.SerializeObject(stickylist);
            System.IO.File.WriteAllText(path, output);
            SaveStickyNotesInBlob();
            return this.Json(true, JsonRequestBehavior.AllowGet);
        }
        public ActionResult UpdateStickyNote(Sticky record)
        {
            string path = Server.MapPath(@"~\Content\StickyNotes.json");
            List<Sticky> stickylist = new List<Sticky>();
            using (WebClient wc = new WebClient())
            {
                var json = wc.DownloadString(path);
                stickylist = JsonConvert.DeserializeObject<List<Sticky>>(json);
            }
            Sticky updateobj = new Sticky();
            updateobj = stickylist.Where(x => x.StickyId == record.StickyId).FirstOrDefault();
            updateobj.Text = record.Text;
            updateobj.Title = record.Title;
            string output = JsonConvert.SerializeObject(stickylist);
            System.IO.File.WriteAllText(path, output);
            SaveStickyNotesInBlob();
            return this.Json(true, JsonRequestBehavior.AllowGet);
        }

        public ActionResult DeleteStickyNote(int id)
        {
            string path = Server.MapPath(@"~\Content\StickyNotes.json");
            List<Sticky> stickylist = new List<Sticky>();
            using (WebClient wc = new WebClient())
            {
                var json = wc.DownloadString(path);
                stickylist = JsonConvert.DeserializeObject<List<Sticky>>(json);
            }
            stickylist.Remove(stickylist.Where(x => x.StickyId == id).FirstOrDefault());
            string output = JsonConvert.SerializeObject(stickylist);
            System.IO.File.WriteAllText(path, output);
            SaveStickyNotesInBlob();
            return this.Json(true, JsonRequestBehavior.AllowGet);
        }
        public void SaveStickyNotesInBlob()
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(RaviKotuwar);
            CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer searchBlobContainer = cloudBlobClient.GetContainerReference("donivanq");
            CloudBlockBlob blob = searchBlobContainer.GetBlockBlobReference("StickyNotes.json");
            blob.UploadFromFile(Server.MapPath(@"~\Content\StickyNotes.json"));
        }
        public void SaveQnAInBlob()
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(RaviKotuwar);
            CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer searchBlobContainer = cloudBlobClient.GetContainerReference("donivanq");
            CloudBlockBlob blob = searchBlobContainer.GetBlockBlobReference("QnAs.json");
            blob.UploadFromFile(Server.MapPath(@"~\Content\QnAs.json"));
        }

        public ActionResult Compare()
        {
            return View();
        }

        public ActionResult SaveImage(string user)
        {
            string path = Server.MapPath("~/Content/ImagePaste/Images/" + user + "/");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            string filename = Guid.NewGuid().ToString();
            HttpPostedFileBase file = Request.Files[0];
            file.SaveAs(path + filename + ".png");
            return Json(true);
        }
        public ActionResult GetImages(string user)
        {
            List<string> imagelist = new List<string>();
            try
            {
                string path = Server.MapPath("~/Content/ImagePaste/Images/" + user + "/");
                if (Directory.Exists(path))
                {
                    imagelist = Directory.GetFiles(path, "*.png").OrderByDescending(d => new FileInfo(d).CreationTime).ToList();
                    return Json(imagelist, JsonRequestBehavior.AllowGet);
                }
                else return Json(imagelist, JsonRequestBehavior.AllowGet);

            }
            catch (Exception)
            {
                return Json(false);
            }
        }

        public ActionResult DeleteImage(string path)
        {

            string rpath = Server.MapPath("~" + path);
            System.IO.File.Delete(rpath);
            return Json(true);
        }

        public ActionResult RunSQLQuery()
        {
            if (Request.Cookies["QueryLogin"] != null)
            {
                ViewBag.Owner = Request.Cookies["QueryLogin"].Values["Owner"];
                ViewBag.Pin = Request.Cookies["QueryLogin"].Values["Pin"];
            }
            else
                ViewBag.Owner = "null";
            return View();
        }
        public JsonResult SQLConnectionCheck(string constr)
        {
            constr = constr.Replace("%20", " ");
            try
            {
                using (SqlConnection con = new SqlConnection(constr))
                {
                    con.Open();
                    return Json(true, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(false, JsonRequestBehavior.AllowGet);
            }
        }
    }

    public class CloudFile
    {
        public string FileName { get; set; }
        public string OriginalName { get; set; }
        public string URL { get; set; }
        public long Size { get; set; }
        public long BlockCount { get; set; }
        public CloudBlockBlob BlockBlob { get; set; }
        public DateTime StartTime { get; set; }
        public string UploadStatusMessage { get; set; }
        public bool IsUploadCompleted { get; set; }
        public string AssetId { get; set; }
        public double Duration { get; set; }
        public string StorageAccount { get; set; }
        public static CloudFile CreateFromIListBlobItem(IListBlobItem item)
        {
            if (item is CloudBlockBlob)
            {
                var blob = (CloudBlockBlob)item;
                return new CloudFile
                {
                    FileName = blob.Name,
                    URL = blob.Uri.ToString(),
                    Size = blob.Properties.Length
                };
            }
            return null;
        }
    }
    public class Sticky
    {
        public int StickyId { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
        public bool Lock { get; set; }
        public string Owner { get; set; }
    }
    public class Sqlcommand
    {
        public int CommandId { get; set; }
        public string Command { get; set; }
        public bool Public { get; set; }
        public string Owner { get; set; }
        public string Pin { get; set; }

    }
    public class SQLResult
    {
        public bool Status { get; set; }
        public string Result { get; set; }
    }
}
