using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MepoverSharedProject.SheetCopier;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MepoverSharedProject.SheetCopier
{
    public class RequestHandler : IExternalEventHandler
    {
        // A trivial delegate, but handy
        //private delegate void DoorOperation(FamilyInstance e);
        RequestMethods helperMethods = null;
        SheetCopierViewModel mainViewModel = null;
        RevitService revitService = null;

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
            return "SheetCopier";
        }

        public RequestHandler(SheetCopierViewModel viewModel, RevitService revitService)
        {
            mainViewModel = viewModel;
            this.revitService = revitService;
            if (helperMethods == null)
            {
                helperMethods = new RequestMethods(mainViewModel, revitService);
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
                    case RequestId.RunRevitAction:
                        {
                            helperMethods.RunRevitAction();
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

        private SheetCopierViewModel mainViewModel;
        private RevitService revitService;
        public RequestMethods(SheetCopierViewModel viewModel, RevitService revitService)
        {
            mainViewModel = viewModel;
            this.revitService = revitService;
        }

        public void RunRevitAction()
        {
            SCDocument selectedDocument = mainViewModel.SelectedDocument;
            List<SCSheet> selectedSheets = selectedDocument.ScSheets.Where(s => s.IsChecked).ToList();
            List<ViewSheet> sheets = selectedSheets.Select(s => s.Sheet).ToList();
            revitService.CopySheets(selectedDocument.Doc, sheets, mainViewModel.AnnotationChecks);
        }


    }
    public enum RequestId : int
    {
        None = 0,

        RunRevitAction = 1,

        ToggleFamilyLoaderEvent = 2,

        ToggleFamilyLoadingEvent = 3,
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
