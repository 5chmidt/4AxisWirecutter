# 4AxisWirecutter
C# tools for Rhino 6 to enable programming of a 4-axis ruled surface cutter.

To install the plugin download the .rhi file and drag and drop onto the Rhino 6 canvas, or run to install.

<h2>Known Issues</h2>

Toolpaths are not correctly translating to the origin.  
For the time being, the best way to ensure toolpaths generate correctly is to draw objects in Rhino on the origin.

<h2>Change Log</h2>

2/26/20 - Resolved issue with toolpathing core, that would skip half of the drive curves is specific situations.

2/8/20 - RESOLVED - When creating multi-line toolpaths.
Where the start of the first curve is not the end of the second curve, the g-code is missing the last line of the first curve.
This is causing the machine to begin jogging to the next toolpath prior to finishing the first cut in its entirity.
</p>

8/15/19 - Changed cutting plane input to specify an origin, normal and rotational axis.  After specifying the rotation of the cut plane, parts will always be translated to the origin correctly before generating toolpaths.

8/15/19 - Changed to default toolpath tolerance to be 0.01" from 0.001" to reduce toolpath geneation time and file size.  Increasing the tolerance will also be more forgiving of modeling that is not a perfectly ruled surface.

8/15/19 - Added method to C# class for generating a toolpath from a set of lines.  This will be used when in the grasshopper version in the future.
