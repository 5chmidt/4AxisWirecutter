# 4AxisWirecutter
C# tools for Rhino 6 to enable programming of a 4-axis ruled surface cutter.

To install the plugin download the .rhi file and drag and drop onto the Rhino 6 canvas, or run to install.

<h2>Known Issues</h2>
If issues are found in testing please submit them via a github issue and/or upload a test file in the bug submission folder.

CHANGE LOG:

<h4>2/8/20 - RESOLVED</h4> When creating multi-line toolpaths.
Where the start of the first curve is not the end of the second curve, the g-code is missing the last line of the first curve.
This is causing the machine to begin jogging to the next toolpath prior to finishing the first cut in its entirity.

8/15/19 - Changed cutting plane input to specify an origin, normal and rotational axis.  After specifying the rotation of the cut plane, parts will always be translated to the origin correctly before generating toolpaths.

8/15/19 - Changed to default toolpath tolerance to be 0.01" from 0.001" to reduce toolpath geneation time and file size.  Increasing the tolerance will also be more forgiving of modeling that is not a perfectly ruled surface.

8/15/19 - Added method to C# class for generating a toolpath from a set of lines.  This will be used when in the grasshopper version in the future.
