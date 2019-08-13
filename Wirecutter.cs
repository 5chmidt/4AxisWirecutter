using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace BVTC.Wirecutter
{
    public class Wirecutter : Command
    {
        

        public Wirecutter()
        {
            // Rhino only creates one instance of each command class defined in a
            // plug-in, so it is safe to store a refence in a static property.
            Instance = this;
        }

        ///<summary>The only instance of this command.</summary>
        public static Wirecutter Instance
        {
            get; private set;
        }

        ///<returns>The command name as it appears on the Rhino command line.</returns>
        public override string EnglishName
        {
            get { return "Wirecutter"; }
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var ui = new UI.Wirecutter(doc);
            Rhino.UI.Dialogs.ShowSemiModal(ui);
            ui.HideCurveDirection();
            return Result.Success;
        }
    }
}
