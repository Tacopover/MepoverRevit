using System;
using System.Windows.Forms;
using WixSharp;
using WixSharp.Forms;

namespace WixSharp_Installer
{
    internal class Program
    {
        const string publisher = "MEPover";
        const string appGuid = "d027893e-c704-41cb-aedf-c95befa90190";
        static void Main()
        {
            string filedir = @"C:\Users\taco\source\repos\MepoverRevit\bin\Release\";

            var project = new ManagedProject("MepoverRevit",
                             new Dir(@"C:\ProgramData\Autodesk\Revit\Addins",
                                 new Dir(@"2021",
                                    new File(@"C:\Users\taco\OneDrive - MEPover\Revit\content\manifests\2021\MepoverRevit.addin"),
                                    new Dir(@"Mepover",
                                        //new File(@"C:\Users\taco\Void Manager\VoidManager - Documents\6. Templates\Revit\00_Families\2021\NLRS_30_ME_UN_void_rectangular_VM.rfa"),
                                        //new File(@"C:\Users\taco\Void Manager\VoidManager - Documents\6. Templates\Revit\00_Families\2021\NLRS_30_ME_UN_void_round_VM.rfa"),
                                        new File(filedir + "MepoverRevit.2021.dll"))),
                                new Dir(@"2022",
                                    new File(@"C:\Users\taco\OneDrive - MEPover\Revit\content\manifests\2022\MepoverRevit.addin"),
                                    new Dir(@"Mepover",
                                        new File(filedir + "MepoverRevit.2022.dll"))),
                                new Dir(@"2023",
                                    new File(@"C:\Users\taco\OneDrive - MEPover\Revit\content\manifests\2023\MepoverRevit.addin"),
                                    new Dir(@"Mepover",
                                        new File(filedir + "MepoverRevit.2023.dll"))),
                                new Dir(@"2024",
                                    new File(@"C:\Users\taco\OneDrive - MEPover\Revit\content\manifests\2024\MepoverRevit.addin"),
                                    new Dir(@"Mepover",
                                        new File(filedir + "MepoverRevit.2024.dll")))))
            {
                InstallScope = InstallScope.perUser,
                GUID = new Guid(appGuid),
                Version = new Version("0.1.0")
            };

            //project.Package.AttributesDefinition = "InstallPrivileges=limited";
            project.ControlPanelInfo.Manufacturer = publisher;
            project.ControlPanelInfo.Contact = "Taco Pover";

            //project.ManagedUI = ManagedUI.Empty;    //no standard UI dialogs
            //project.ManagedUI = ManagedUI.Default;  //all standard UI dialogs

            //custom set of standard UI dialogs
            project.ManagedUI = new ManagedUI();

            project.ManagedUI.InstallDialogs.Add(Dialogs.Welcome)
                                            .Add(Dialogs.Licence)
                                            //.Add(Dialogs.SetupType)
                                            //.Add(Dialogs.Features)
                                            //.Add(Dialogs.InstallDir)
                                            .Add(Dialogs.Progress)
                                            .Add(Dialogs.Exit);

            //project.ManagedUI.ModifyDialogs.Add(Dialogs.MaintenanceType)
            //                               .Add(Dialogs.Features)
            //                               .Add(Dialogs.Progress)
            //                               .Add(Dialogs.Exit);

            project.MajorUpgrade = new MajorUpgrade()
            {
                Schedule = UpgradeSchedule.afterInstallInitialize,
                DowngradeErrorMessage = $"A later version of {publisher} is already installed. Setup will now exit."
            };

            project.Load += Msi_Load;
            project.BeforeInstall += Msi_BeforeInstall;
            project.AfterInstall += Msi_AfterInstall;
            project.LicenceFile = @"C:\Users\taco\OneDrive - MEPover\Revit\License file.rtf";
            project.BannerImage = @"C:\Users\taco\OneDrive - MEPover\Taco\dev\resources\Icons\MEPover banner.png";
            //project.ValidateBackgroundImage = false;
            project.BackgroundImage = @"C:\Users\taco\OneDrive - MEPover\Taco\dev\resources\Icons\MEPover background image.png";

            //project.SourceBaseDir = "<input dir path>";
            //project.OutDir = "<output dir path>";

            project.BuildMsi();
        }

        static void Msi_Load(SetupEventArgs e)
        {
            if (!e.IsUISupressed && !e.IsUninstalling)
                MessageBox.Show(e.ToString(), "Load");
        }

        static void Msi_BeforeInstall(SetupEventArgs e)
        {
            if (!e.IsUISupressed && !e.IsUninstalling)
                MessageBox.Show(e.ToString(), "BeforeInstall");
        }

        static void Msi_AfterInstall(SetupEventArgs e)
        {
            if (!e.IsUISupressed && !e.IsUninstalling)
                MessageBox.Show(e.ToString(), "AfterExecute");
        }

    }
}