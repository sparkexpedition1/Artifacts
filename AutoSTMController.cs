using DataBench.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Mvc;

namespace AutoSTM.Controllers
{
    public class AutoSTMController : Controller
    {
        // GET: AutoSTM
        WebClient wc = new WebClient();
        
        public ActionResult test()
        {
            return View();
        }
        public ActionResult Landing()
        {
            return View();
        }
        public ActionResult Index()
        {
            return View();
        }
        public bool SQLConnectionCheck(string constr)
        {
            constr = constr.Replace("%20", " ");
            try
            {
                using (SqlConnection con = new SqlConnection(constr))
                {
                    con.Open();
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                return false;
            }
        }
        public JsonResult GetSourceTarget()
        {
            WebClient wc = new WebClient();
            string json = wc.DownloadString(Server.MapPath("~/Content/Config/AutoSTM/DataSources.json"));
            SourceTarget st = JsonConvert.DeserializeObject<SourceTarget>(json);
            foreach (var item in st.Sources)
                item.ConnectionLive = SQLConnectionCheck(item.ConnectionString);
            foreach (var item in st.Targets)
                item.ConnectionLive = SQLConnectionCheck(item.ConnectionString);
            return Json(st, JsonRequestBehavior.AllowGet);
        }
        public JsonResult GetSavedDatSources()
        {
            List<SavedDataSource> list = new List<SavedDataSource>();
            try
            {
                string path = Server.MapPath("~/Content/Config/AutoSTM/DataSources.json");
                WebClient wc = new WebClient();
                list = JsonConvert.DeserializeObject<List<SavedDataSource>>(wc.DownloadString(path));
                foreach (var item in list)
                {
                    item.ConnectionLive = SQLConnectionCheck(item.ConnectionString);
                }
                return Json(list, JsonRequestBehavior.AllowGet);
            }
            catch (Exception)
            {
                return Json(list, JsonRequestBehavior.AllowGet);
            }
        }

        public JsonResult SaveDataSource(SavedDataSource obj,bool isSource)
        {
            try
            {
                string path = Server.MapPath("~/Content/Config/AutoSTM/DataSources.json");
                bool exits = false;
                if (obj.ConnectionType == "SQL") { exits = SQLConnectionCheck(obj.ConnectionString); }
                else exits = true;
                if (exits)
                {
                    SourceTarget dataobj= JsonConvert.DeserializeObject<SourceTarget>(wc.DownloadString(path));
                    List<SavedDataSource> list;
                    if(isSource )
                        list= dataobj.Sources;
                    else 
                        list= dataobj.Targets;
                    obj.SourceId = list.Count > 0 ? list.Select(x => x.SourceId).Max() + 1 : 1;
                    list.Add(obj);
                    System.IO.File.WriteAllText(path, JsonConvert.SerializeObject(dataobj));
                    return Json(true);
                }
                else return Json(false);

            }
            catch (Exception)
            {
                return Json(false);
            }

        }
        public JsonResult UpdateDataSource(SavedDataSource obj, bool isSource)
        {
            try
            {
                string path = Server.MapPath("~/Content/Config/AutoSTIM/DataSources.json");
                SourceTarget dataobj = JsonConvert.DeserializeObject<SourceTarget>(wc.DownloadString(path));
                List<SavedDataSource> list;
                if (isSource)
                    list = dataobj.Sources;
                else
                    list = dataobj.Targets;
                SavedDataSource updateobj = list.Where(x => x.SourceId == obj.SourceId).FirstOrDefault();
                updateobj.ConnectionFriendlyName = obj.ConnectionFriendlyName;
                updateobj.ConnectionString = obj.ConnectionString;
                updateobj.Owner = obj.Owner;
                updateobj.ConnectionType = obj.ConnectionType;
                System.IO.File.WriteAllText(path, JsonConvert.SerializeObject(dataobj));
                return Json(true);
            }
            catch (Exception)
            {
                return Json(false);
            }

        }
        public JsonResult DeleteDataSource(int sourceid,bool isSource)
        {
            try
            {
                string path = Server.MapPath("~/Content/Config/AutoSTM//DataSources.json");
                SourceTarget dataobj = JsonConvert.DeserializeObject<SourceTarget>(wc.DownloadString(path));
                List<SavedDataSource> list;
                if (isSource)
                    list = dataobj.Sources;
                else
                    list = dataobj.Targets;
                list.Remove(list.Where(x => x.SourceId == sourceid).FirstOrDefault());
                System.IO.File.WriteAllText(path, JsonConvert.SerializeObject(dataobj));
                return Json(true);
            }
            catch (Exception)
            {
                return Json(false);
            }

        }
        public JsonResult ApplyDefaults()
        {
            //ConfigModel filterobj = JsonConvert.DeserializeObject<ConfigModel>(System.IO.File.ReadAllText(Server.MapPath("~/Content/LargeDBConfig.json")));
            //List<SchemaData> schema = JsonConvert.DeserializeObject<List<SchemaData>>(System.IO.File.ReadAllText(Server.MapPath("~/Content/Schema.json")));
            //List<OutConfig> outconfiglist = new List<OutConfig>();
            //foreach (SchemaData item in schema)
            //{
            //    string existkey = filterobj.DefaultRules.Keys.Where(x => item.ColumnName.Contains(x)).FirstOrDefault();
            //    if (existkey != null)
            //    {
            //        string[] values = filterobj.DefaultRules[existkey].Split(',');
            //        foreach (string value in values)
            //        {
            //            OutConfig existobj = outconfiglist.Where(x => x.Schema == item.SchemaName && x.Table == item.TableName && x.Column == item.ColumnName && x.Function == value).FirstOrDefault();
            //            if (existobj == null)
            //            {
            //                outconfiglist.Add(new OutConfig()
            //                {
            //                    Database = "LargeDB",
            //                    Schema = item.SchemaName,
            //                    Table = item.TableName,
            //                    Column = item.ColumnName,
            //                    Function = value,
            //                    Parameters = new List<OutParameter>() { new OutParameter { ParameterName = "IgnoreNulls", Value = item.IsNullable.ToString() } }
            //                });
            //            }
            //        }
            //    }
            //    IndividualRule irules = filterobj.IndividualRules.Where(x => x.Schema == item.SchemaName && x.TableName == item.TableName).FirstOrDefault();
            //    if (irules != null)
            //    {
            //        string existikey = irules.Rules.Keys.Where(x => item.ColumnName.Contains(x)).FirstOrDefault();
            //        if (existikey != null)
            //        {
            //            string[] values = irules.Rules[existikey].Split(',');
            //            foreach (string value in values)
            //            {
            //                OutConfig existobj = outconfiglist.Where(x => x.Schema == item.SchemaName && x.Table == item.TableName && x.Column == item.ColumnName && x.Function == value).FirstOrDefault();
            //                if (existobj == null)
            //                {
            //                    outconfiglist.Add(new OutConfig()
            //                    {
            //                        Database = "LargeDB",
            //                        Schema = item.SchemaName,
            //                        Table = item.TableName,
            //                        //Column = string.Join("", Regex.Split(item.ColumnName, @"(?:\r\n)")),
            //                        Column = item.ColumnName,
            //                        Function = value,
            //                        Parameters = new List<OutParameter>() { new OutParameter { ParameterName = "IgnoreNulls", Value = item.IsNullable.ToString() } }
            //                    });
            //                }
            //            }

            //        }

            //    }
            //}
            //string csv = JsonToCsv(JsonConvert.SerializeObject(outconfiglist));
            //System.IO.File.WriteAllText(Server.MapPath("~/Content/DQ_Output.csv"), csv);
            return Json(true, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetNoteOutput()
        {
            string[] lines = System.IO.File.ReadAllLines(Server.MapPath("~/Content/Config/AutoSTM//DataComparison.csv"));
            var jsonResult = Json(CsvToJson(lines), JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }
        public string CsvToJson(string[] lines)
        {
            // Get lines.
            if (lines.Length < 2) throw new InvalidDataException("Must have header line.");
            // Get headers.
            string[] headers = lines.First().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[");
            for (int i = 1; i < lines.Length; i++)
            {
                var fields = lines[i].Split(',');
                if (fields.Length != headers.Length) throw new InvalidDataException("Field count must match header count. at line " + i);
                var jsonElements = headers.Zip(fields, (header, field) => string.Format("{0}: {1}", '"' + header + '"', _isNumericRegex.IsMatch(field) ? field : '"' + field + '"')).ToArray();
                string jsonObject = "{" + string.Format("{0}", string.Join(",", jsonElements)) + "}";
                if (i < lines.Length - 1)
                    jsonObject += ",";
                sb.AppendLine(jsonObject);
            }
            sb.AppendLine("]");
            return sb.Replace("\r\n", "").ToString();
        }
        static readonly Regex _isNumericRegex = new Regex("^(" +
                /*Hex*/ @"0x[0-9a-f]+" + "|" +
                /*Bin*/ @"0b[01]+" + "|" +
                /*Oct*/ @"0[0-7]*" + "|" +
                /*Dec*/ @"((?!0)|[-+]|(?=0+\.))(\d*\.)?\d+(e\d+)?" +
                ")$");
    }
}