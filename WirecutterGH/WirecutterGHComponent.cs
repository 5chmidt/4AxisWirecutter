// In order to load the result of this wizard, you will also need to
// add the output bin/ folder of this project to the list of loaded
// folder in Grasshopper.
// You can use the _GrasshopperDeveloperSettings Rhino command for that.

namespace WirecutterGH
{
    using System;
    using System.Collections.Generic;
    using Grasshopper.Kernel;
    using Rhino.Geometry;
    using GH = Grasshopper;

    public class WirecutterGHComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WirecutterGHComponent"/> class.
        /// Each implementation of GH_Component must provide a public
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear,
        /// Subcategory the panel. If you use non-existing tab or panel names,
        /// new tabs/panels will automatically be created.
        /// </summary>
        public WirecutterGHComponent()
          : base("WirecutterGH", "Nickname", "Description", "Category", "Subcategory")
        {
        }

        /// <summary>
        /// Gets the guid.  Each component must have a unique Guid to identify it.
        /// It is vital this Guid doesn't change otherwise old ghx files
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("d9d47f5c-0412-40f1-904b-b6658143dfc5"); }
        }

        /// <summary>
        /// Gets the Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                // You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH.Kernel.GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPlaneParameter("P", "P", "Base Plane", GH.Kernel.GH_ParamAccess.tree, Plane.WorldYZ);
            pManager.AddLineParameter("L", "L", "Spacing", GH.Kernel.GH_ParamAccess.item);
            pManager.AddTextParameter("F", "F", "Toolpath Folder", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // register outputs here.
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
        }
    }
}
