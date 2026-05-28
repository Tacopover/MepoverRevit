using ClashDetector.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace ClashDetector
{
    public class ClashSettings
    {
        public bool IsCategoriesEnabled { get; set; }
        public ObservableCollection<ListItem> Categories1 { get; set; } = new ObservableCollection<ListItem>();
        public ObservableCollection<ListItem> Categories2 { get; set; } = new ObservableCollection<ListItem>();

        public ObservableCollection<ListItem> RevitModels1 { get; set; } = new ObservableCollection<ListItem>();
        public ObservableCollection<ListItem> RevitModels2 { get; set; } = new ObservableCollection<ListItem>();

        public ClashSettings()
        {
            AddCategories();
        }

        public void AddCategories()
        {
            Categories1.Add(new ListItem("Air Terminals"));
            Categories1.Add(new ListItem("Cable Trays", true));
            Categories1.Add(new ListItem("Cable Tray Fittings"));
            Categories1.Add(new ListItem("Communication Devices"));
            Categories1.Add(new ListItem("Conduits"));
            Categories1.Add(new ListItem("Conduit Fittings"));
            Categories1.Add(new ListItem("Data Devices"));
            Categories1.Add(new ListItem("Ducts", true));
            Categories1.Add(new ListItem("Duct Accessories"));
            Categories1.Add(new ListItem("Duct Fittings"));
            Categories1.Add(new ListItem("Electrical Equipment"));
            Categories1.Add(new ListItem("Electrical Fixtures"));
            Categories1.Add(new ListItem("Fire Alarm Devices"));
            Categories1.Add(new ListItem("Flex Ducts"));
            Categories1.Add(new ListItem("Flex Pipes"));
            Categories1.Add(new ListItem("Generic Models"));
            Categories1.Add(new ListItem("Pipes", true));
            Categories1.Add(new ListItem("Lighting Fixtures"));
            Categories1.Add(new ListItem("Lighting Devices"));
            Categories1.Add(new ListItem("Mechanical Equipment"));
            Categories1.Add(new ListItem("Nurse Call Devices"));
            Categories1.Add(new ListItem("Pipe Accessories"));
            Categories1.Add(new ListItem("Pipe Fittings"));
            Categories1.Add(new ListItem("Plumbing Fixtures"));
            Categories1.Add(new ListItem("Security Devices"));
            Categories1.Add(new ListItem("Specialty Equipment"));
            Categories1.Add(new ListItem("Sprinklers"));
            Categories1.Add(new ListItem("Telephone Devices"));

            Categories2.Add(new ListItem("Casework"));
            Categories2.Add(new ListItem("Ceilings"));
            Categories2.Add(new ListItem("Columns", true));
            Categories2.Add(new ListItem("Curtain Panels"));
            Categories2.Add(new ListItem("Curtain Wall Mullions"));
            Categories2.Add(new ListItem("Doors", true));
            Categories2.Add(new ListItem("Entourage"));
            Categories2.Add(new ListItem("Floors"));
            Categories2.Add(new ListItem("Furniture"));
            Categories2.Add(new ListItem("Furniture Systems"));
            Categories2.Add(new ListItem("Generic Models"));
            Categories2.Add(new ListItem("Parts"));
            Categories2.Add(new ListItem("Railings"));
            Categories2.Add(new ListItem("Ramps"));
            Categories2.Add(new ListItem("Roads"));
            Categories2.Add(new ListItem("Roofs"));
            Categories2.Add(new ListItem("Specialty Equipment"));
            Categories2.Add(new ListItem("Stairs", true));
            Categories2.Add(new ListItem("Structural Columns", true));
            Categories2.Add(new ListItem("Structural Foundations"));
            Categories2.Add(new ListItem("Structural Framing"));
            Categories2.Add(new ListItem("Walls", true));
            Categories2.Add(new ListItem("Windows"));
        }
    }
}
