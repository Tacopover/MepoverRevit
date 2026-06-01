using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClashDetector
{
    public class RequestHandler : IExternalEventHandler
    {
        // A trivial delegate, but handy
        //private delegate void DoorOperation(FamilyInstance e);
        RequestMethods helperMethods = null;
        RevitClashService revitService = null;

        // The value of the latest request made by the modeless form 
        private Request m_request = new Request();

        /// <summary>
        /// A public property to access the current request value
        /// </summary>
        public Request Request
        {
            get { return m_request; }
        }

        /// <summary>
        ///   A method to identify this External Event Handler
        /// </summary>
        public String GetName()
        {
            return "ClashDetector";
        }

        public RequestHandler(RevitClashService revitService)
        {
            this.revitService = revitService;
            if (helperMethods == null)
            {
                helperMethods = new RequestMethods(revitService);
            }


        }

        /// <summary>
        ///   The top method of the event handler.
        /// </summary>
        /// <remarks>
        ///   This is called by Revit after the corresponding
        ///   external event was raised (by the modeless form)
        ///   and Revit reached the time at which it could call
        ///   the event's handler (i.e. this object)
        /// </remarks>
        /// 
        public void Execute(UIApplication uiapp)
        {

            try
            {
                switch (Request.Take())
                {
                    case RequestId.None:
                        {
                            return;  // no request at this time -> we can leave immediately
                        }
                    case RequestId.RunRevitClashes:
                        {
                            helperMethods.RunRevitAction();
                            break;
                        }
                    case RequestId.SelectElements:
                        {
                            helperMethods.SelectElementsAction();
                            break;
                        }
                    case RequestId.ZoomElements:
                        {
                            helperMethods.ZoomElementsAction();
                            break;
                        }


                    default:
                        {
                            throw new Exception("Unknown command issued to the RequestHandler");
                        }
                }
            }
            catch (Exception ex)
            {
                string msg = ex.ToString();

                TaskDialog.Show("error", msg);
            }
            finally
            {
                //RevitCommand.mainEntry.WakeFormUp();
            }

            return;
        }

    }

    public class RequestMethods
    {

        private RevitClashService revitService;
        public RequestMethods(RevitClashService revitService)
        {
            this.revitService = revitService;
        }

        public void RunRevitAction()
        {
            revitService.ExecuteClashRun();
        }

        public void SelectElementsAction()
        {
            revitService.ExecuteSelectInOpenModel();
        }

        public void ZoomElementsAction()
        {
            revitService.ExecuteZoomTo();
        }


    }
    public enum RequestId : int
    {
        None = 0,

        RunRevitClashes = 1,

        ToggleFamilyLoaderEvent = 2,

        ToggleFamilyLoadingEvent = 3,

        SelectElements = 4,

        ZoomElements = 5,
    }


    public class Request
    {

        private int m_request = (int)RequestId.None;


        public RequestId Take()
        {
            return (RequestId)Interlocked.Exchange(ref m_request, (int)RequestId.None);
        }

        public void Make(RequestId request)
        {
            Interlocked.Exchange(ref m_request, (int)request);
        }
    }
}
