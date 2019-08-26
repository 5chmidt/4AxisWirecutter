# 4AxisWirecutter
C# tools for Rhino 6 to enable programming of a 4-axis ruled surface cutter.

To install the plugin download the .rhi file and drag and drop onto the Rhino 6 canvas, or run to install.

Grasshopper Plugin:
	Currenly a work in progress.  More info coming soon.

CHANGE LOG:

8/15/19 - Changed cutting plane input to specify an origin, normal and rotational axis.  After specifying the rotation of the cut plane, parts will always be translated to the origin correctly before generating toolpaths.

8/15/19 - Changed to default toolpath tolerance to be 0.01" from 0.001" to reduce toolpath geneation time and file size.  Increasing the tolerance will also be more forgiving of modeling that is not a perfectly ruled surface.

8/15/19 - Added method to C# class for generating a toolpath from a set of lines.  This will be used when in the grasshopper version in the future.