using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MepoverSharedProject.SheetCopier.Commands;
using MepoverSharedProject.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using UIFramework;

namespace MepoverSharedProject.SheetCopier
{
    public class SheetCopierViewModel : BaseViewModel
    {
        #region images
        private System.Windows.Media.ImageSource _minimizeImage;
        public System.Windows.Media.ImageSource MinimizeImage
        {
            get { return _minimizeImage; }
            set
            {
                _minimizeImage = value;
                OnPropertyChanged(nameof(MinimizeImage));
            }
        }
        private System.Windows.Media.ImageSource _maximizeImage;
        public System.Windows.Media.ImageSource MaximizeImage
        {
            get { return _maximizeImage; }
            set
            {
                _maximizeImage = value;
                OnPropertyChanged(nameof(MaximizeImage));
            }
        }
        private System.Windows.Media.ImageSource _closeImage;
        public System.Windows.Media.ImageSource CloseImage
        {
            get { return _closeImage; }
            set
            {
                _closeImage = value;
                OnPropertyChanged(nameof(CloseImage));
            }
        }

        #endregion

        private readonly UIApplication uiApp;
        private Autodesk.Revit.ApplicationServices.Application m_app;
        private Document m_doc;
        private RevitService revitService;
        //private string windowState;

        RequestHandler handler;
        ExternalEvent exEvent;

        public ICollectionView collView { get; set; }
        Dictionary<string, Predicate<SCSheet>> criteriaMap;
        List<DataGridTextColumnTPO> filteredColumns = new List<DataGridTextColumnTPO>();

        public Dictionary<string, bool> AnnotationChecks;
        public bool IsWindowClosed { get; set; } = true;

        #region properties
        private SheetCopierWindow _mainWindow;
        public SheetCopierWindow MainWindow
        {
            get
            {
                if (_mainWindow == null)
                {
                    _mainWindow = new SheetCopierWindow() { DataContext = this };
                }
                return _mainWindow;
            }
            set
            {
                _mainWindow = value;
                OnPropertyChanged(nameof(MainWindow));
            }
        }

        private SCDocument _selectedDocument;
        public SCDocument SelectedDocument
        {
            get
            {
                return _selectedDocument;
            }
            set
            {
                _selectedDocument = value;
                LinkedSheets.Clear();
                foreach (SCSheet sheet in SelectedDocument.ScSheets)
                {
                    LinkedSheets.Add(sheet);
                }
                OnPropertyChanged(nameof(SelectedDocument));
            }
        }
        private ObservableCollection<SCDocument> _linkedDocuments;
        public ObservableCollection<SCDocument> LinkedDocuments
        {
            get
            {
                if (_linkedDocuments == null)
                {
                    _linkedDocuments = new ObservableCollection<SCDocument>();
                }
                return _linkedDocuments;
            }
            set
            {
                _linkedDocuments = value;
                OnPropertyChanged(nameof(LinkedDocuments));
            }
        }

        private ObservableCollection<SCSheet> _linkedSheets;
        public ObservableCollection<SCSheet> LinkedSheets
        {
            get
            {
                if (_linkedSheets == null)
                {
                    _linkedSheets = new ObservableCollection<SCSheet>();
                }
                return _linkedSheets;
            }
            set { _linkedSheets = value; OnPropertyChanged(nameof(LinkedSheets)); }
        }

        private bool _copyAnnotations;
        public bool CopyAnnotations
        {
            get { return _copyAnnotations; }
            set { _copyAnnotations = value; OnPropertyChanged(nameof(CopyAnnotations)); }
        }

        private bool _copyLines;
        public bool CopyLines
        {
            get { return _copyLines; }
            set { _copyLines = value; OnPropertyChanged(nameof(CopyLines)); }
        }

        private bool _copyTextNotes;
        public bool CopyTextNotes
        {
            get { return _copyTextNotes; }
            set { _copyTextNotes = value; OnPropertyChanged(nameof(CopyTextNotes)); }
        }

        private bool _copyRegions;
        public bool CopyRegions
        {
            get { return _copyRegions; }
            set { _copyRegions = value; OnPropertyChanged(nameof(CopyRegions)); }
        }

        private bool _copyDetailItems;
        public bool CopyDetailItems
        {
            get { return _copyDetailItems; }
            set { _copyDetailItems = value; OnPropertyChanged(nameof(CopyDetailItems)); }
        }

        private bool _copyDimensions;
        public bool CopyDimensions
        {
            get { return _copyDimensions; }
            set { _copyDimensions = value; OnPropertyChanged(nameof(CopyDimensions)); }
        }

        #endregion

        #region Commands
        public RelayCommand<object> RunCommand { get; set; }

        #endregion

        public SheetCopierViewModel(UIApplication uiapp)
        {
            uiApp = uiapp;
            m_app = uiApp.Application;
            m_doc = uiapp.ActiveUIDocument.Document;
            if (revitService == null)
            {
                revitService = new RevitService(uiapp);
            }

            handler = new RequestHandler(this, revitService);
            exEvent = ExternalEvent.Create(handler);

            RunCommand = new RelayCommand<object>(p => true, p => RunCommandAction());
            PopulateModels();
            collView = CollectionViewSource.GetDefaultView(LinkedSheets);

            MinimizeImage = ConvertToEmbeddedImage("minimizeButton.png");
            MaximizeImage = ConvertToEmbeddedImage("maximizeButton.png");
            CloseImage = ConvertToEmbeddedImage("closeButton.png");
            ShowMainWindow();
        }

        public void ShowMainWindow()
        {
            if (IsWindowClosed)
            {
                MainWindow = new SheetCopierWindow() { DataContext = this };
                handler = new RequestHandler(this, revitService);
                exEvent = ExternalEvent.Create(handler);
                WindowInteropHelper helper = new WindowInteropHelper(MainWindow);
                helper.Owner = uiApp.MainWindowHandle;
                MainWindow.Show();
                IsWindowClosed = false;
                MainWindow.Closed += MainWindow_Closed;
            }
            else
            {
                MainWindow.Activate();
            }
        }
        private void MainWindow_Closed(object sender, EventArgs e)
        {
            exEvent.Dispose();
            exEvent = null;
            handler = null;
            IsWindowClosed = true;
            MainWindow.Closed -= MainWindow_Closed;
        }


        private void RunCommandAction()
        {
            SetAnnotationChecks();
            MakeRequest(RequestId.RunRevitAction);
        }

        private void SetAnnotationChecks()
        {
            if (AnnotationChecks == null)
            {
                AnnotationChecks = new Dictionary<string, bool>();
            }
            AnnotationChecks["CopyAnnotations"] = CopyAnnotations;
            AnnotationChecks["CopyLines"] = CopyLines;
            AnnotationChecks["CopyTextNotes"] = CopyTextNotes;
            AnnotationChecks["CopyRegions"] = CopyRegions;
            AnnotationChecks["CopyDetailItems"] = CopyDetailItems;
            AnnotationChecks["CopyDimensions"] = CopyDimensions;
        }

        private void PopulateModels()
        {
            List<Document> documents = revitService.OpenDocuments;
            foreach (Document doc in documents)
            {
                SCDocument scDoc = new SCDocument(doc);

                List<Element> sheets = revitService.GetSheets(doc);
                foreach (Element sheet in sheets)
                {
                    ViewSheet viewsheet = sheet as ViewSheet;
                    scDoc.ScSheets.Add(new SCSheet(viewsheet));
                }
                LinkedDocuments.Add(scDoc);
            }

        }

        public void MakeRequest(RequestId request)
        {
            handler.Request.Make(request);
            exEvent.Raise();
        }

        public void FilterHeader(string filterText, DataGridColumnHeader columnHeader)
        {

            DataGridColumn column = columnHeader.Column;
            if (column == null || !(column is DataGridBoundColumn boundColumn))
            {
                return;
            }

            System.Windows.Data.Binding binding = boundColumn.Binding as System.Windows.Data.Binding;
            if (binding == null)
            {
                return;
            }

            if (criteriaMap == null)
            {
                criteriaMap = new Dictionary<string, Predicate<SCSheet>>();
            }
            string columnName = columnHeader.Content.ToString();

            if (criteriaMap.ContainsKey(columnName))
            {
                criteriaMap.Remove(columnName);
            }

            string propertyName = binding.Path.Path;

            Predicate<SCSheet> predicate = (c) =>
            {
                object value = GetNestedPropertyValue(c, propertyName);
                if (value != null)
                {
                    if (IsNumericType(value.GetType()))
                    {
                        return value.ToString().Contains(filterText, StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        return value.ToString().Contains(filterText, StringComparison.OrdinalIgnoreCase);
                    }
                }
                return false;
            };

            criteriaMap.Add(columnName, predicate);
            if (column is DataGridTextColumnTPO textColumn)
            {
                filteredColumns.Add(textColumn);
            }


            collView.Filter = o =>
            {
                SCSheet item = o as SCSheet;
                return criteriaMap.Values.ToList().TrueForAll(x => x(item));
            };

            collView.Refresh();
        }

        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);
            if (parent == null)
                return null;

            if (parent is T typedParent)
                return typedParent;

            return FindParent<T>(parent);
        }

        public void ClearFilters()
        {
            if (criteriaMap == null) return;
            criteriaMap.Values.ToList().Clear();
            collView.Filter = null;
            collView.Refresh();
        }

        private bool IsNumericType(Type type)
        {
            return type == typeof(int) || type == typeof(double) || type == typeof(float) || type == typeof(decimal);
        }
        private object GetNestedPropertyValue(object obj, string propertyPath)
        {
            string[] properties = propertyPath.Split('.');
            foreach (string propertyName in properties)
            {
                PropertyInfo property = obj.GetType().GetProperty(propertyName);
                if (property == null)
                    return null;

                obj = property.GetValue(obj);
                if (obj == null)
                    return null;
            }
            return obj;
        }

        private System.Windows.Media.ImageSource ConvertToEmbeddedImage(string imagePath)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var img = new System.Windows.Media.Imaging.BitmapImage();
            return Utils.LoadEmbeddedImage(assembly, imagePath);
        }
    }

    public static class StringExtensions
    {
        public static bool Contains(this string source, string toCheck, StringComparison comp)
        {
            return source?.IndexOf(toCheck, comp) >= 0;
        }
    }

    public class SCDocument
    {
        public string Name { get; set; }
        public Document Doc { get; set; }
        public ObservableCollection<SCSheet> ScSheets { get; set; }
        public bool IsChecked { get; set; }

        public SCDocument(Document doc)
        {
            Name = doc.Title;
            Doc = doc;
            IsChecked = false;
            if (ScSheets == null)
            {
                ScSheets = new ObservableCollection<SCSheet>();
            }
        }
    }

    public class SCSheet
    {
        public string Number { get; set; }
        public string Name { get; set; }
        public ViewSheet Sheet { get; set; }
        public bool IsChecked { get; set; }

        public SCSheet(ViewSheet sheet)
        {
            Name = sheet.Name;
            Number = sheet.SheetNumber;
            Sheet = sheet;
            IsChecked = false;
        }
    }
}
