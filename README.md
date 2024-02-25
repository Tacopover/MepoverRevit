## Description
This Revit plugin hosts the SheetCopier plugin. I have taken this [post](https://archi-lab.net/how-to-maintain-revit-plugins-for-multiple-versions-continued/) by the great Konrad Sobon as inspiration for the multi version shared project setup. I think this is a really clean solution for handling multiple versions of Revit. 
The installer is created with WixSharp. There are some references to my local drive in that project for the background images and license file that will need to be replaced if you intend to create your own installer. The installer itself can be found in the Release section.
## SheetCopier description
Allows the user to select sheets from a linked file and then copy those sheets along with its views to the host model. The plugin uses WPF with the MVVM pattern and Revit's ExternalEvent to handle a modeless dialog (even though a modeless dialog is not really necessary for the functionality of this plugin).
The plugin is still very much a work in progress, but it functions at a basic level. It is intended for beginning Revit API developers to explore more advanced topics like modeless dialogs, some WPF styling and the MVVM pattern. On the other hand I realize that I am far from a professional developer, so comments from the pros are more then welcome as well!
Here is what the plugin can do and cannot do at the moment:

### Features
- Copies sheets between models
- Copies views and placing them in the same location and rotation on the sheet
- Copies legends
- Copies schedules
- Tries to find a title block with the same family name and type in the host
- Offers the option to copy
	- Generic Annotations
	- Detail Items
	- Filled Regions
	- Dimensions
	- Text Notes
	- Detail Lines

### Does not (yet) do
- Copy views at the correct location is the model origins do not align
- Copy Reflected Ceiling Plans
- Copy Dependent view
- Copy Tags
- Copy callouts in Revit 2021
- Copy sheet/title block shared parameters
- And maybe other stuff that I did not run into while testing

I realize that this plugin will only be useful in rather specific cases, but it was created because it was needed for an actual project, so I'm hoping it might help out a few other people who run into a similar situation as well.
