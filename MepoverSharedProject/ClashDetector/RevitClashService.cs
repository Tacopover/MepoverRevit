using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using Autodesk.Revit.UI;
using ClashDetector.Models;
using ClashDetector.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ClashDetector
{
    public class RevitClashService
    {
        private NamedPipeServerStream pipeServer;
        private StreamReader reader;
        private StreamWriter writer;
        private const int MaxRetries = 5;
        private const int DelayMilliseconds = 1000;

        RequestHandler handler;
        ExternalEvent exEvent;

        private Document doc;
        private Options geomOptions = new Options();
        public ClashSettings Settings { get; set; } = new ClashSettings();
        public UIApplication UIApp { get; private set; }
        public Dictionary<string, Document> DocumentMap { get; private set; }
        public Dictionary<string, RevitLinkInstance> RevitLinkInstanceMap { get; private set; }

        //private ClashWindow clashWindow;
        //public ClashWindow ClashWindow
        //{
        //    get
        //    {
        //        if (clashWindow == null)
        //        {
        //            clashWindow = new ClashWindow();
        //            clashWindow.ViewModel.ResultChanged += OnResultChanged;
        //        }
        //        return clashWindow;
        //    }
        //}

        private string result;
        public string Result
        {
            get { return result; }
            set { result = value; }
        }


        public RevitClashService(UIApplication uiapp)
        {
            UIApp = uiapp;
            doc = UIApp.ActiveUIDocument.Document;
            handler = new RequestHandler(this);
            exEvent = ExternalEvent.Create(handler);
        }

        public void Initialize()
        {
            PopulateLinkedModels();
            PopulateSettings();
            //StartServer();
            //ClashWindow.Show();
            //ClashWindow testWindow = new ClashWindow();
            //testWindow.Show();

        }

        #region IPC Window
        private async void StartServer()
        {
            int retries = 0;
            bool pipeCreated = false;

            while (retries < MaxRetries && !pipeCreated)
            {
                try
                {
                    if (NamedPipeHelper.IsPipeBusy("RevitChat"))
                    {
                        NamedPipeHelper.TerminatePipe("RevitChat");
                    }

                    ClosePipe();

                    if (pipeServer == null || !pipeServer.IsConnected)
                    {
                        pipeServer = new NamedPipeServerStream("RevitChat", PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                    }
                    pipeCreated = true;


                    string processName = "RevitClashDetectorUI"; // Name of the process to check
                    var existingProcesses = System.Diagnostics.Process.GetProcessesByName(processName);

                    if (existingProcesses.Length == 0)
                    {
                        Process.Start("C:\\Users\\taco\\source\\repos\\MepoverRevit\\RevitClashDetectorUI\\bin\\Debug\\net8.0-windows\\RevitClashDetectorUI.exe");
                    }

                    await pipeServer.WaitForConnectionAsync();

                    //MessagesTextBox.AppendText("Connected to client.\n");

                    writer = new StreamWriter(pipeServer) { AutoFlush = true };
                    reader = new StreamReader(pipeServer);

                    ListenForMessages();

                }
                catch (IOException ex) when (ex.Message.Contains("All pipe instances are busy"))
                {
                    retries++;
                    Console.WriteLine($"Pipe is busy. Retrying {retries}/{MaxRetries}...");
                    Thread.Sleep(DelayMilliseconds);
                }
            }

            if (!pipeCreated)
            {
                Console.WriteLine("Failed to create named pipe server after multiple attempts.");
            }


        }

        public async void SendMessage(string message)
        {
            if (pipeServer != null && pipeServer.IsConnected)
            {
                await writer.WriteLineAsync(message);
            }
            else
            {
                MessageBox.Show("Not connected to client");
            }
        }

        private async void ListenForMessages()
        {
            while (pipeServer.IsConnected)
            {
                string message = await reader.ReadLineAsync();
                if (message == "Run Clashes")
                {
                    //Application.Current.Dispatcher.Invoke(() =>
                    //{
                    //    MakeRequest(RequestId.RunRevitAction);
                    //}
                    //);
                    MakeRequest(RequestId.RunRevitClashes);
                }
            }
        }

        public void ClosePipe()
        {

            // Dispose of the pipeClient and other resources
            if (pipeServer != null)
            {
                if (pipeServer.IsConnected)
                {
                    pipeServer.WaitForPipeDrain();
                }
                pipeServer.Dispose();
                pipeServer = null;
            }

            if (reader != null)
            {
                reader.Dispose();
                reader = null;
            }

            if (writer != null)
            {
                writer.Dispose();
                writer = null;
            }

        }

        #endregion

        #region logic for starting UI as reference dll

        //private void StartAndConnectToWindow()
        //{
        //    uiThread = new Thread(() =>
        //    {
        //        mainWindow = new MainWindow();
        //        mainWindow.RunFromRevit = true;
        //        mainWindow.Closed += (s, e) => System.Windows.Threading.Dispatcher.ExitAllFrames();
        //        mainWindow.Show();
        //        System.Windows.Threading.Dispatcher.Run();
        //    });

        //    uiThread.SetApartmentState(ApartmentState.STA);
        //    uiThread.Start();
        //}

        //// Method to close the MainWindow
        //public void CloseMainWindow()
        //{
        //    if (mainWindow != null)
        //    {
        //        mainWindow.Dispatcher.Invoke(() =>
        //        {
        //            mainWindow.Close();
        //        });

        //        uiThread.Join();
        //    }
        //}

        #endregion

        public List<Clash> RunClashes()
        {
            ElementCollector collector = new ElementCollector(UIApp.ActiveUIDocument);
            Dictionary<string, List<Element>> ModelMap = collector.GetVisibleElements();

            List<string> docTitlesA = Settings.RevitModels1.Where(cat => cat.IsSelected).Select(cat => cat.Name).ToList();
            List<string> docTitlesB = Settings.RevitModels2.Where(cat => cat.IsSelected).Select(cat => cat.Name).ToList();

            List<Clash> clashes = new List<Clash>();

            foreach (string titleA in docTitlesA)
            {
                List<Element> startElementsA;
                if (!ModelMap.TryGetValue(titleA, out startElementsA))
                {
                    continue;
                }

                List<Element> elementsA = new List<Element>();
                if (Settings.IsCategoriesEnabled)
                {
                    foreach (string category in Settings.Categories1.Where(cat => cat.IsSelected).Select(cat => cat.Name).ToList())
                    {
                        List<Element> elements = startElementsA.Where(e => e.Category.Name == category).ToList();
                        if (elements.Count > 0)
                        {
                            elementsA.AddRange(elements);
                        }
                    }
                }
                else
                {
                    elementsA = startElementsA;
                }


                Document documentA;
                RevitLinkInstance linkInstanceA;
                Transform tranformA;
                if (RevitLinkInstanceMap.TryGetValue(titleA, out linkInstanceA))
                {
                    tranformA = linkInstanceA.GetTotalTransform();
                }
                else
                {
                    tranformA = Transform.Identity;
                }

                if (!DocumentMap.TryGetValue(titleA, out documentA))
                {
                    continue;
                }

                foreach (string titleB in docTitlesB)
                {
                    List<Element> startElementsB;
                    if (!ModelMap.TryGetValue(titleB, out startElementsB))
                    {
                        continue;
                    }

                    List<Element> elementsB = new List<Element>();
                    if (Settings.IsCategoriesEnabled)
                    {
                        foreach (string category in Settings.Categories1.Where(cat => cat.IsSelected).Select(cat => cat.Name).ToList())
                        {
                            List<Element> elements = startElementsB.Where(e => e.Category.Name == category).ToList();
                            if (elements.Count > 0)
                            {
                                elementsB.AddRange(elements);
                            }
                        }
                    }
                    else
                    {
                        elementsB = startElementsB;
                    }

                    Document documentB;
                    RevitLinkInstance linkInstanceB;
                    Transform tranformB;
                    if (RevitLinkInstanceMap.TryGetValue(titleB, out linkInstanceB))
                    {
                        tranformB = linkInstanceB.GetTotalTransform();
                    }
                    else
                    {
                        tranformB = Transform.Identity;
                    }

                    if (!DocumentMap.TryGetValue(titleB, out documentB))
                    {
                        continue;
                    }

                    foreach (Element elementA in elementsA)
                    {
                        foreach (Element elementB in elementsB)
                        {
                            Clash clash = getClash(documentA, documentB, elementA, elementB, tranformA, tranformB);
                            if (clash != null)
                            {
                                clashes.Add(clash);
                            }
                        }
                    }
                }

            }
            SendMessage(clashes.Count.ToString() + " clashes found");
            return clashes;

        }


        private Clash getClash(Document doc1, Document doc2, Element element1, Element element2, Transform transformLink1, Transform transformLink2)
        {
            Clash clash = null;
            MEPCurve mepcurve = element2 as MEPCurve;

            BoundingBoxXYZ bboxSub = element2.get_BoundingBox(null);
            if (bboxSub == null)
            {
                return clash;
            }
            BoundingBoxXYZ bboxAlignedSub = new BoundingBoxXYZ();
            bboxAlignedSub.Min = transformLink2.OfPoint(bboxSub.Min);
            bboxAlignedSub.Max = transformLink2.OfPoint(bboxSub.Max);
            XYZ location_ft = null;
            double rotation = 0;

            Location location = element2.Location;
            if (location == null)
            {
                return clash;
            }
            if (location is LocationPoint)
            {
                LocationPoint locationSub = element2.Location as LocationPoint;
                rotation = locationSub.Rotation;
            }
            else if (location is LocationCurve)
            {
                LocationCurve locationSub = element2.Location as LocationCurve;
                Line curveSub = GetElementCurve(element2) as Line;
                if (curveSub == null)
                {
                    return clash;
                }
                XYZ direction = curveSub.Direction;
                double dotProduct = direction.DotProduct(XYZ.BasisY);
                rotation = Math.Acos(dotProduct);
            }
            else
            {
                return clash;
            }


            BoundingBoxXYZ bboxSuper = element1.get_BoundingBox(null);
            if (bboxSuper == null)
            {
                return clash;
            }
            BoundingBoxXYZ bboxAlignedSuper = new BoundingBoxXYZ();

            bboxAlignedSuper.Min = transformLink1.OfPoint(bboxSuper.Min);
            bboxAlignedSuper.Max = transformLink1.OfPoint(bboxSuper.Max);
            Solid subSolid = null;
            Solid superSolid = null;
            Solid diffSolid = null;

            if (!BboxIntersects(bboxAlignedSub, bboxAlignedSuper))
            {
                return clash;
            }

            string clashType = element1.Category.ToString();
            //check for intersection with solids to make sure they actually clash // TODO getElementSolid in a try/catch
            Solid originSubSolid = null;
            try
            {
                originSubSolid = GetElementSolid(element2, geomOptions);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not clash due to error in creating solid from element: " + element2.Id + " in " + doc2.Title);
            }

            if (originSubSolid == null)
            {
                //use boundingbox for creating solid. Less accurate but better than nothing.
                BoundingBoxXYZ originBboxSub = element2.get_BoundingBox(null);
                originSubSolid = SolidByBoundingBox(originBboxSub);
            }
            Solid originSuperSolid = null;
            try
            {
                originSuperSolid = GetElementSolid(element1, geomOptions);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not get element solid");
            }
            if (originSuperSolid == null)
            {
                //use boundingbox for creating solid. Less accurate but better than nothing.
                BoundingBoxXYZ originBboxSuper = element1.get_BoundingBox(null);
                originSuperSolid = SolidByBoundingBox(originBboxSuper);
            }
            subSolid = SolidUtils.CreateTransformed(originSubSolid, transformLink2);
            superSolid = SolidUtils.CreateTransformed(originSuperSolid, transformLink1);
            //TODO use a geometry engine that does not throw errors when calculating solid intersections
            try
            {
                diffSolid = BooleanOperationsUtils.ExecuteBooleanOperation(subSolid, superSolid, BooleanOperationsType.Intersect);
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                //TODO write elements to error report for user
                return clash;
            }
            //TODO what to do with really small diffSolids? could be a really small clash between 2 bigger elements or a really small subElement that can be ignored
            // f.i. pipe/duct fitting placeholders or information carriers.
            if (diffSolid.Volume > 0.00001)
            {
                location_ft = diffSolid.ComputeCentroid();
            }
            if (location_ft == null)
            {
                return clash;
            }

            clash = new Clash(doc1, doc2, element1, element2, location_ft, rotation);

            clash.TypeOfClash = clashType;
            return clash;
        }

        private void PopulateSettings()
        {
            foreach (string title in DocumentMap.Keys)
            {
                Settings.RevitModels1.Add(new ListItem(title));
                Settings.RevitModels2.Add(new ListItem(title));
            }
        }
        public void PopulateLinkedModels()
        {
            DocumentMap = new Dictionary<string, Document>();
            RevitLinkInstanceMap = new Dictionary<string, RevitLinkInstance>();
            //Add host model to dictionary first
            DocumentMap.Add(doc.Title, doc);

            //then add all the linked models
            IList<Element> linkedInstances = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).ToElements();
            foreach (Element linkElement in linkedInstances)
            {
                RevitLinkInstance linkedInstance = linkElement as RevitLinkInstance;
                if (RevitLinkType.IsLoaded(doc, linkedInstance.GetTypeId()))
                {
                    Document linkdoc = linkedInstance.GetLinkDocument();
                    DocumentMap.Add(linkdoc.Title, linkdoc);
                    RevitLinkInstanceMap.Add(linkdoc.Title, linkedInstance);
                }
            }
        }

        public Curve GetElementCurve(Element element)
        {
            if (element.Location is LocationCurve)
            {
                LocationCurve curve = element.Location as LocationCurve;
                return curve.Curve;
            }
            return null;
        }

        public bool BboxIntersects(BoundingBoxXYZ box1, BoundingBoxXYZ box2)
        {
            XYZ min1 = box1.Min;
            XYZ max1 = box1.Max;
            XYZ min2 = box2.Min;
            XYZ max2 = box2.Max;
            bool result = (min1.X <= max2.X && max1.X >= min2.X) && (min1.Y <= max2.Y && max1.Y >= min2.Y) && (min1.Z <= max2.Z && max1.Z >= min2.Z);
            return result;
        }

        public Solid GetElementSolid(Element element, Options geomOptions)
        {
            GeometryElement geomElement = element.get_Geometry(geomOptions);
            List<Solid> solids = new List<Solid>();
            Solid union = null;
            foreach (GeometryObject geomobject in geomElement)
            {
                Solid solid = geomobject as Solid;
                if (solid != null && solid?.Volume > 0)
                {
                    solids.Add(solid);
                }
                GeometryInstance geomInst = geomobject as GeometryInstance;
                if (null == geomInst)
                {
                    continue;
                }
                foreach (GeometryObject geom in geomInst.GetInstanceGeometry())
                {
                    Solid subsolid = geom as Solid;
                    if (subsolid != null || subsolid?.Volume > 0)
                    {
                        solids.Add(subsolid);
                    }
                }
            }
            List<Solid> failedSolids = new List<Solid>();
            foreach (Solid subsolid in solids)
            {
                if (null == union)
                {
                    union = subsolid;
                }
                else
                {
                    try
                    {
                        union = BooleanOperationsUtils.ExecuteBooleanOperation(union, subsolid, BooleanOperationsType.Union);
                    }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                    {
                        failedSolids.Add(subsolid);
                        continue;
                    }
                }
            }
            if (union != null && failedSolids.Any())
            {
                foreach (Solid failedSolid in failedSolids)
                {
                    try
                    {
                        union = BooleanOperationsUtils.ExecuteBooleanOperation(union, failedSolid, BooleanOperationsType.Union);
                    }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                    {
                        throw new Exception("Could not create Solid from Element");
                    }
                }
            }

            return union;
        }

        public Solid SolidByBoundingBox(BoundingBoxXYZ bbox)
        {
            // corners in BBox coords
            XYZ pt0 = new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Min.Z);
            XYZ pt1 = new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Min.Z);
            XYZ pt2 = new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Min.Z);
            XYZ pt3 = new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Min.Z);
            //edges in BBox coords
            Line edge0 = Line.CreateBound(pt0, pt1);
            Line edge1 = Line.CreateBound(pt1, pt2);
            Line edge2 = Line.CreateBound(pt2, pt3);
            Line edge3 = Line.CreateBound(pt3, pt0);
            //create loop, still in BBox coords
            List<Curve> edges = new List<Curve>();
            edges.Add(edge0);
            edges.Add(edge1);
            edges.Add(edge2);
            edges.Add(edge3);
            Double height = bbox.Max.Z - bbox.Min.Z;
            CurveLoop baseLoop = CurveLoop.Create(edges);
            List<CurveLoop> loopList = new List<CurveLoop>();
            loopList.Add(baseLoop);
            Solid transformBox = GeometryCreationUtilities.CreateExtrusionGeometry(loopList, XYZ.BasisZ, height);

            return transformBox;

        }

        public void MakeRequest(RequestId request)
        {
            handler.Request.Make(request);
            exEvent.Raise();
        }
    }
}
