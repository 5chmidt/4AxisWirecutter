// Developed by Boston Valley Terra Cotta.
namespace BVTC.RhinoTools
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.IO;
    using Rhino;
    using Rhino.Geometry;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Xml.Linq;
    using BVTC.osTools.Helpers;

    /// <summary>
    /// Class used to generate .nc code for 4-axis wirecutting.
    /// </summary>
    public class Wirecutter
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Wirecutter"/> class.
        /// </summary>
        /// <param name="dt">DataTable - must contain correct column structure for toolpathing inputs.</param>
        /// <param name="offset">double - the retract offset specified by the programmer.</param>
        public Wirecutter(RhinoDoc doc, DataTable dt, Rhino.Geometry.Plane plane, double offset)
        {
            this.DefaultSetup();
            this.CutPlane = plane;
            this.Doc = doc;
            this.Parameters = dt;
            this.Xform = Rhino.Geometry.Transform.PlaneToPlane(plane, this.CutPlane);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Wirecutter"/> class.
        /// </summary>
        public Wirecutter(RhinoDoc doc)
        {
            this.Doc = doc;
            this.DefaultSetup();
        }

        /// <summary>
        /// Gets or sets the speed for cutting toolpaths (inches per minute).
        /// </summary>
        public int CuttingSpeed { get; set; }

        /// <summary>
        /// Gets or sets the prefix for cutting speed, typically G01.
        /// </summary>
        public string CutPrefix { get; set; }

        /// <summary>
        /// Gets or sets the currently set cut plane.
        /// </summary>
        public Plane CutPlane { get; set; }

        /// <summary>
        /// Gets or sets a message if there is an error generating a toolpath, to be viewed by the end user.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Gets or sets the feed rate prefix, typically F.
        /// </summary>
        public string FeedPrefix { get; set; }

        /// <summary>
        /// Gets or sets the location of the NC file that is in progress.
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Gets the currently loaded NC code as a DataTable.
        /// </summary>
        public DataTable NCTable
        {
            get { return this.GCode; }
        }

        /// <summary>
        /// Gets or sets the machine code footer that will be applied following all movement instructions.
        /// </summary>
        public string NCFooter { get; set; }

        /// <summary>
        /// Gets or sets the machine code header that will be applied before all movement instructions.
        /// </summary>
        public string NCHeader { get; set; }

        /// <summary>
        /// Gets or sets the prefix for rapid movement, typically G00.
        /// </summary>
        public string RapidPrefix { get; set; }

        /// <summary>
        /// Gets or sets the minumum accurate measurement default: 0.001".
        /// </summary>
        public double Tolerance { get; set; }

        /// <summary>
        /// Gets or sets the number of decimal places to calculate tolerance.
        /// </summary>
        public int ToleranceDecimals { get; set; }

        private string[] AxisPrefixes { get; set; }

        private double[] CurrentLocation { get; set; }

        private RhinoDoc Doc { get; set; }

        private DataTable GCode { get; set; }

        private DataTable Parameters { get; set; }

        private Point3d PointFromXElement(XElement element, string ElementName = "")
        {
            Point3d pt = new Point3d();

            // find the element by name //
            if (!string.IsNullOrEmpty(ElementName))
            {
                element = element.Element(ElementName);
                if (element == null)
                {
                    throw new Exception($"Could not find an element named {ElementName}");
                }
            }

            // first format check for comma seperated values //
            if (!element.Value.Contains(','))
            {
                throw new Exception($"{element.Value} is not the proper format to make a 3D point.");
            }

            // make sure there are three comma seperated values //
            string[] splits = element.Value.Split(',');
            if (splits.Length != 3)
            {
                throw new Exception($"{element.Value} is not the proper format to make a 3D point.");
            }

            for (int i = 0; i < splits.Length; i++)
            {
                double d;
                if (!double.TryParse(splits[i], out d))
                {
                    throw new Exception($"{element.Value} is not the proper format to make a 3D point.");
                }

                pt[i] = d;
            }

            return pt;
        }

        private Transform Xform { get; set; }

        /// <summary>
        /// Exports the current toolpathing settings into an xml file.
        /// </summary>
        /// <param name="filePath">string - the target location for the settings file.</param>
        /// <returns>bool - true if the file was successfully created.</returns>
        public bool ImportSettings(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Could not file file at: {filePath}");
            }

            XElement settingsFile = XElement.Load(filePath);

            var settings = settingsFile.Element("Settings");
            if (settings == null)
            {
                throw new Exception($"{filePath} does not contain a 'Settings' element.");
            }

            foreach (var element in settings.Elements())
            {
                var prop = this.GetType().GetProperty(element.Name.LocalName);
                if (prop == null)
                {
                    continue;
                }

                // set integer properties //
                if (prop.PropertyType == typeof(int))
                {
                    int i;
                    if (int.TryParse(element.Value, out i))
                    {
                        prop.SetValue(this, i);
                    }
                }

                // set double properties //
                if (prop.PropertyType == typeof(double))
                {
                    double d;
                    if (double.TryParse(element.Value, out d))
                    {
                        prop.SetValue(this, d);
                    }
                }

                // set string properties //
                if (prop.PropertyType == typeof(string))
                {
                    prop.SetValue(this, element.Value);
                }
            }

            // import plane //
            var planeXml = settingsFile.Element("Plane");
            if (planeXml == null)
            {
                return false;
            }

            // origin point //
            this.CutPlane = new Plane(
                this.PointFromXElement(planeXml, "Origin"),
                (Vector3d)this.PointFromXElement(planeXml, "xDirection"),
                (Vector3d)this.PointFromXElement(planeXml, "yDirection"));

            return true;
        }

        /// <summary>
        /// Pulls data out of the xml file into the datatable for UI display.
        /// </summary>
        /// <param name="filePath">string - the location of the XML file.</param>
        /// <param name="dt">DataTable - This should be an empty table with the columns you want to fill in.</param>
        /// <returns>DataTable - The input DataTable with applicable columns copied over.</returns>
        public DataTable ImportCurveTable(string filePath, DataTable dt)
        {
            XElement settingsFile = XElement.Load(filePath);

            var id = settingsFile.Element("DocId");
            if (id == null)
            {
                return dt;
            }

            Guid guid;
            if (!Guid.TryParse(id.Value, out guid))
            {
                return dt;
            }

            if (guid != Document.GetGuid(this.Doc, false))
            {
                throw new Exception($"The settings file was created for a different Rhino file.");
            }

            // build curve table //
            var curveTable = settingsFile.FindElement("CurveTable");
            foreach (var node in curveTable.Elements())
            {
                var row = dt.NewRow();
                for (int col = 0; col < row.Table.Columns.Count; col++)
                {
                    string columnName = row.Table.Columns[col].ColumnName;
                    if (columnName.Contains("HiddenColumn"))
                    {
                        string camelCase = Regex.Replace(columnName, "([a-z](?=[A-Z])|[A-Z](?=[A-Z][a-z]))", "$1 ");
                        row[col] = camelCase.Split(' ').FirstOrDefault();
                    }

                    var item = node.Element(columnName);
                    if (item == null)
                    {
                        continue;
                    }

                    Type type = row.Table.Columns[col].DataType;
                    object obj;
                    if (type == typeof(Guid))
                    {
                        Guid g = new Guid();
                        if (!Guid.TryParse(item.Value, out g))
                        {
                            continue;
                        }

                        obj = g;
                    }
                    else
                    {
                        obj = Convert.ChangeType(item.Value, type);
                    }

                    row[col] = obj;
                }

                dt.Rows.Add(row);
            }

            return dt;
        }

        /// <summary>
        /// Imports toolpathing settings from an xml file.
        /// </summary>
        /// <param name="filePath">string - the location of the newly created setting file.</param>
        /// <returns>true - if a new file was created.</returns>
        public bool ExportSettings(string filePath, DataTable CurveTable)
        {
            // create an xml file with a specific id embedded in the document //
            XDocument document = new XDocument();
            var root = new XElement("Root");
            Guid docId = RhinoTools.Document.GetGuid(this.Doc, true);
            var id = new XElement("DocId", docId);
            root.Add(id);

            // save properties //
            var settings = new XElement("Settings");
            foreach (var property in this.GetType().GetProperties())
            {
                if (property.PropertyType == typeof(int)
                    || property.PropertyType == typeof(double)
                    || property.PropertyType == typeof(string))
                {
                    settings.Add(new XElement(property.Name, property.GetValue(this)));
                }
            }

            root.Add(settings);

            // save cut plane to settings file //
            var plane = new XElement("Plane");
            plane.Add(new XElement("Origin", this.CutPlane.Origin));
            plane.Add(new XElement("xDirection", this.CutPlane.XAxis));
            plane.Add(new XElement("yDirection", this.CutPlane.YAxis));
            root.Add(plane);

            // save curve selections //
            if (CurveTable.Rows.Count > 0)
            {
                var curveTable = new XElement("CurveTable");
                for (int row = 0; row < CurveTable.Rows.Count; row++)
                {
                    var curveSet = new XElement("CurveSet");
                    for (int col = 0; col < CurveTable.Columns.Count; col++)
                    {
                        if (CurveTable.Columns[col].ColumnName.Contains("HiddenColumn"))
                        {
                            continue;
                        }

                        curveSet.Add(new XElement(
                            CurveTable.Columns[col].ColumnName,
                            CurveTable.Rows[row][col]));
                    }

                    curveTable.Add(curveSet);
                }

                root.Add(curveTable);
            }

            document.Add(root);
            document.Save(filePath);
            return true;
        }

        /// <summary>
        /// Generate a list of curves that show the toolpath movement.
        /// </summary>
        /// <param name="onOrigin">If true generate the toolpaths at world Origin.</param>
        /// <returns></returns>
        public Line[] ToolpathToCurves(bool onOrigin, double lineLength = 6)
        {
            List<Line> lines = new List<Line>();
            foreach (DataRow row in this.GCode.Rows)
            {
                double x = (double)row["X"];
                double y = (double)row["Y"];
                double z = (double)row["Z"];
                double angle = (double)row["A"];

                Transform xform = Transform.Rotation(RhinoMath.ToRadians(angle), this.CutPlane.Normal, new Point3d(x, y, z));
                Point3d pt0 = new Point3d(x, y, z) + (this.CutPlane.XAxis * lineLength);
                Point3d pt1 = new Point3d(x, y, z) - (this.CutPlane.XAxis * lineLength);
                pt0.Transform(xform);
                pt1.Transform(xform);

                Line line = new Line(pt0, pt1);
                if (!onOrigin && this.Xform.TryGetInverse(out xform))
                {
                    line.Transform(xform);
                }

                lines.Add(line);
            }

            return lines.ToArray();
        }

        /// <summary>
        /// Generate a preview mesh of the toolpath cuts.
        /// </summary>
        /// <param name="onOrigin"></param>
        /// <returns>Mesh - Rhino geometry.</returns>
        public Mesh[] ToolpathToMesh(bool onOrigin)
        {
            Line[] lines = this.ToolpathToCurves(onOrigin);
            List<Mesh> meshes = new List<Mesh>();

            for (int i = 0; i < lines.Length - 1; i++)
            {
                Mesh mesh = new Mesh();
                Line first = lines[i];
                Line second = lines[i + 1];

                // check if the curves are intersection //
                double a;
                double b;
                if (Rhino.Geometry.Intersect.Intersection.LineLine(first, second, out a, out b, this.Tolerance, true))
                {
                    // create two triangular meshes //
                    Point3d intersect = first.PointAt(a);
                    
                    // add verticies //
                    mesh.Vertices.Add(first.ToNurbsCurve().PointAtStart);
                    mesh.Vertices.Add(second.ToNurbsCurve().PointAtStart);
                    mesh.Vertices.Add(intersect);
                    mesh.Vertices.Add(first.ToNurbsCurve().PointAtEnd);
                    mesh.Vertices.Add(second.ToNurbsCurve().PointAtEnd);

                    // add faces //
                    mesh.Faces.AddFace(0, 1, 2);
                    mesh.Faces.AddFace(3, 1, 4);
                    mesh.Normals.ComputeNormals();
                    mesh.Compact();
                    meshes.Add(mesh);
                }
                else
                {
                    // add single quad mesh //
                    mesh.Vertices.Add(first.ToNurbsCurve().PointAtStart);
                    mesh.Vertices.Add(second.ToNurbsCurve().PointAtStart);
                    mesh.Vertices.Add(second.ToNurbsCurve().PointAtEnd);
                    mesh.Vertices.Add(first.ToNurbsCurve().PointAtEnd);
                    mesh.Faces.AddFace(0, 1, 2, 3);
                    mesh.Normals.ComputeNormals();
                    mesh.Compact();
                    meshes.Add(mesh);
                }
            }

            return meshes.ToArray();
        }

        /// <summary>
        /// Loads an .NC file into this.NCTable DataTable format.
        /// </summary>
        /// <param name="filePath">string - filePath of the nc code.</param>
        /// <returns>bool - true if file was loaded return true.</returns>
        public bool LoadGCodeFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Cannot find toolpath file at: '{filePath}'");
            }

            // reset gcode settings //
            this.DefaultSetup();
            using (FileStream fileStream = File.OpenRead(filePath))
            {
                using (var streamReader = new StreamReader(fileStream))
                {
                    string line;
                    while ((line = streamReader.ReadLine()) != null)
                    {
                        // don't add setup lines to positioning table //
                        if (line.Contains("G54"))
                        {
                            continue;
                        }

                        DataRow row = this.GCode.NewRow();
                        // Is line a cut or a jog motion //
                        if (line.StartsWith("G00"))
                        {
                            row["Type"] = "Retract";
                        }
                        else
                        {
                            row["Type"] = "Cut";
                        }

                        for (int i = 0; i < this.AxisPrefixes.Length; i++)
                        {
                            // check each axis for a command //
                            string axisPrefix = this.AxisPrefixes[i];
                            int index = line.IndexOf(axisPrefix);
                            if (index < 0)
                            {
                                // movement not required for this axis //
                                row[i + 1] = this.CurrentLocation[i];
                                continue;
                            }

                            // save the axis movements to the datatable //
                            index += axisPrefix.Length;
                            int advance = line.Substring(index, line.Length - index).IndexOf(' ');
                            double position = double.Parse(line.Substring(index, advance));
                            row[i + 1] = position;
                            this.CurrentLocation[i] = position;
                        }

                        this.GCode.Rows.Add(row);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Method to combine parameters of the toolpath class to write a simple text file containing machine instructions.
        /// </summary>
        /// <param name="path">string - the filepath where the gcode will be output.</param>
        /// <returns>bool - On success true, otherwise false.</returns>
        public bool DriveCrvsToToolpath(string path)
        {
            try
            {
                this.ValidateInputTable();

                foreach (DataRow row in this.Parameters.Rows)
                {
                    string type = (string)row["Type"];
                    if (type == "Single")
                    {
                        Guid id = (Guid)row["Curve0"];
                        var obj = this.Doc.Objects.Find(id);
                        if (obj.IsValid && obj.Geometry != null && obj.Geometry.ObjectType == Rhino.DocObjects.ObjectType.Curve)
                        {
                            var curve = (Rhino.Geometry.Curve)obj.Geometry;
                            curve.Transform(this.Xform);
                            DataRow gcode = this.CalculatePostion(curve.PointAtStart, curve.PointAtEnd);
                            this.GCode.Rows.Add(gcode);
                        }

                        continue;
                    }

                    Guid[] ids = new Guid[]
                    {
                        (Guid)row["Curve0"],
                        (Guid)row["Curve1"],
                    };
                    Guid id1 = (Guid)row["Curve1"];
                    bool extend = (bool)row["Extend"];

                    // TODO: impliment method to extend curves to boundry //
                    if (extend)
                    {
                        throw new NotImplementedException("Method for extending curves has not yet been implimented.");
                    }

                    List<Rhino.Geometry.Curve> curves = new List<Rhino.Geometry.Curve>();
                    List<Rhino.Geometry.Polyline> polylines = new List<Rhino.Geometry.Polyline>();
                    foreach (Guid guid in ids)
                    {
                        var obj = this.Doc.Objects.Find(guid);
                        if (obj.IsValid && obj.Geometry != null && obj.Geometry.ObjectType == Rhino.DocObjects.ObjectType.Curve)
                        {
                            var curve = (Rhino.Geometry.Curve)obj.Geometry;
                            if (!curve.IsValid)
                            {
                                throw new Exception($"Curve id: {guid} is not a valid curve.");
                            }

                            curve.Transform(this.Xform);
                            var crv = curve.ToPolyline(this.Tolerance, this.Tolerance, this.Tolerance, 100);

                            if (crv == null)
                            {
                                throw new Exception($"Something is fucked up with curve id: {guid}" +
                                    $"{Environment.NewLine}Try exploding and re-joining the curve." +
                                    $"{Environment.NewLine}No on knows why this fixes the issue, but it seems to.");
                            }

                            var polyline = crv.ToPolyline();
                            curve.Domain = new Rhino.Geometry.Interval(0, 1);
                            curves.Add(curve);
                            polylines.Add(polyline);
                        }
                        else
                        {
                            throw new TypeAccessException($"Object Id: {guid} is not a valid Curve.");
                        }
                    }

                    // begin toolpathing //
                    double[] u = new double[] { 0, 0 };
                    int[] segments = new int[] { 0, 0 };
                    while (segments[0] <= polylines[0].SegmentCount && segments[1] <= polylines[1].SegmentCount)
                    {
                        int first = 0;
                        int second = 1;

                        Point3d[] pts = new Point3d[] { polylines[first][segments[first]], polylines[second][segments[second]] };
                        double[] distances = new double[] {
                            this.CutPlane.DistanceTo(pts[first]),
                            this.CutPlane.DistanceTo(pts[second]),
                        };

                        if (Math.Abs(distances[first] - distances[second]) < this.Tolerance)
                        {
                            segments[first] += 1;
                            segments[second] += 1;
                        }
                        else
                        {
                            // the first curve is always the "drive" curve //
                            // flip the drive curve if the next point isn't on the first curve //
                            if (distances[first] > distances[second])
                            {
                                first = 1;
                                second = 0;
                                pts[first] = pts[second];
                            }

                            var plane = new Plane(pts[first], this.CutPlane.Normal);
                            var intersects = Rhino.Geometry.Intersect.Intersection.CurvePlane(curves[1], plane, this.Tolerance);
                            if (intersects == null)
                            {
                                pts[second] = this.CutPlane.ClosestPoint(pts[1]);
                            }

                            if (intersects[0].IsPoint)
                            {
                                pts[second] = intersects[0].PointA;
                            }
                            else
                            {
                                throw new Exception($"Not method exists to parse intersect type for overlapping curves.");
                            }

                            segments[first] += 1;
                        }

                        DataRow gcode = this.CalculatePostion(pts[first], pts[second]);
                        gcode["Type"] = type;

                        // make sure line of gcode do not repeat //
                        if (this.GCode.Rows.Count > 1)
                        {
                            DataRow lastRow = this.GCode.Rows[this.GCode.Rows.Count - 1];
                            if (Math.Abs((double)gcode["X"] - (double)lastRow["X"]) < this.Tolerance
                                && Math.Abs((double)gcode["Y"] - (double)lastRow["Y"]) < this.Tolerance
                                && Math.Abs((double)gcode["Z"] - (double)lastRow["Z"]) < this.Tolerance
                                && Math.Abs((double)gcode["A"] - (double)lastRow["A"]) < this.Tolerance)
                            {
                                continue;
                            }
                        }

                        this.GCode.Rows.Add(gcode);
                    }
                }

                this.FilePath = path;
                this.WriteGcodeFile();
            }
            catch (Exception e)
            {
                this.ErrorMessage = e.Message;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Generate toolpaths with a set of lines which the wire will follow.
        /// </summary>
        /// <param name="lines">Lines for wire to follow, mid point will be the position, ends will inform the angle.</param>
        /// <param name="types">If the string is empty all values will default to Drive speed, specify "Rapid" for G00.</param>
        /// <returns>If successful returns true.</returns>
        public bool LinesToToolpath(Line[] lines, string[] types)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Transform(this.Xform))
                {
                    throw new Exception($"Unable to transform line to origin.{this.Xform.ToString()}");
                }

                DataRow row = this.CalculatePostion(lines[i].From, lines[i].To);
                if (types.Length == lines.Length && types[i] == "Retract")
                {
                    row["Type"] = "Retract";
                }
                else
                {
                    row["Type"] = "Drive";
                }
            }

            return true;
        }

        /// <summary>
        /// Calcuate the wire rotataion from -360 to +360 based on the input vector and current location.
        /// </summary>
        /// <param name="vector">Vector3d - The direction the wire should be oriented toward.</param>
        /// <returns>double - angle measurement from -360 to +360.</returns>
        public double VectorRotation(Vector3d vector)
        {
            /*
                Locate the Angle of a Vector from 0 to 360
                Using:
                        A<dot> B =|| A || *|| B || *cos(Angle)
                Cosine Law Computes between 0 and 180.
                Vertical Referance allows(+/ -) orientation to be solved.

                H_Len - Magnitude of Horizontal Referance
                H_DP - Dot Product of Vector and Horizontal
                H_Angle - Angle from Horizontal to Vector

                V_Len - Magnitude of Vertical Referance
                V_DP - Dot Product of Vector and Vertical
                V_Angle - Angle from Vertical to Vector

                Quadrent Key:
                    I - H_Angle + V_Angle == 90
                    II - H_Angle - V_Angle == 90
                    III - H_Angle + V_Angle == 270
                    IV - V_Angle - H_Angle == 90

                ###########################
                ##                       ##
                ##     II    |    I      ##
                ##       \   |   /       ##
                ##        \  |  /        ##
                ##^        \ | /         ##
                ##0_________\|/__________##
                ##          /|\          ##
                ##         / | \         ##
                ##        /  |  \        ##
                ##       /   |   \       ##
                ##     III   |   IV      ##
                ##                       ##
                ###########################
            */
            Vector3d horizontal = this.CutPlane.XAxis;
            Vector3d vertical = this.CutPlane.YAxis;

            if (this.CutPlane.Normal.IsParallelTo(vector) != 0)
            {
                throw new Exception($"Wire angle vector cannot be parallel to the cut plane normal!");
            }

            if (!this.CutPlane.Normal.IsPerpendicularTo(vector))
            {
                vector = (Vector3d)this.CutPlane.ClosestPoint(new Point3d(vector.X, vector.Y, vector.Z));
            }

            vector.Unitize();
            var hAngle = RhinoMath.ToDegrees(Vector3d.VectorAngle(vector, horizontal));
            var vAngle = RhinoMath.ToDegrees(Vector3d.VectorAngle(vector, vertical));

            double angle;
            if (vector.IsParallelTo(this.CutPlane.XAxis) != 0)
            {
                angle = 0;
            }
            else if (vector.IsParallelTo(this.CutPlane.YAxis) != 0)
            {
                angle = 90;
            }
            else if (Math.Abs(hAngle + vAngle - 90) <= this.Doc.ModelAbsoluteTolerance)
            {
                // Quadrent I //
                angle = hAngle;
            }
            else if (Math.Abs(hAngle - vAngle - 90) <= this.Doc.ModelAbsoluteTolerance)
            {
                // Quadrend II //
                angle = 90 + vAngle;
            }
            else if (Math.Abs(hAngle + vAngle - 270) <= this.Doc.ModelAbsoluteTolerance)
            {
                // Quadrend III //
                angle = 90 + vAngle;
            }
            else if (Math.Abs(vAngle - hAngle - 90) <= this.Doc.ModelAbsoluteTolerance)
            {
                // Quadrend IV //
                angle = 360 - hAngle;
            }
            else
            {
                throw new Exception("Error calcuating wire position");
            }

            double move = angle - this.CurrentLocation[3];
            for (int adj = -360; adj <= 360; adj += 180)
            {
                if (Math.Abs(move) > Math.Abs(move + adj))
                {
                    move = move + (double)adj;
                }
            }

            return Math.Round(this.CurrentLocation[3] + move, this.ToleranceDecimals);
        }

        private void DefaultSetup()
        {
            this.NCHeader = "G54";
            this.NCFooter = "M30";

            this.CurrentLocation = new double[] { 0, 0, 0, 0 };
            this.AxisPrefixes = new string[] { "X ", "Y ", "Z ", "Q1=" };
            this.FeedPrefix = "F";
            this.CutPrefix = "G01   ";
            this.CuttingSpeed = 200;
            this.RapidPrefix = "G00   ";
            this.CutPlane = Rhino.Geometry.Plane.WorldYZ;
            this.Tolerance = 0.01;
            this.ToleranceDecimals = 3;

            this.NewGCodeTable();
        }

        private DataTable NewGCodeTable()
        {
            // build toolpath datatable //
            this.GCode = new DataTable("GCode");
            this.GCode.Columns.Add("Type", typeof(string));
            this.GCode.Columns.Add("X", typeof(double));
            this.GCode.Columns.Add("Y", typeof(double));
            this.GCode.Columns.Add("Z", typeof(double));
            this.GCode.Columns.Add("A", typeof(double));
            return this.GCode;
        }

        private DataRow CalculatePostion(Point3d pt0, Point3d pt1)
        {
            DataRow position = this.GCode.NewRow();
            Point3d mid = (pt0 + pt1) / 2;

            position["X"] = Math.Round(mid.X, this.ToleranceDecimals);
            position["Y"] = Math.Round(mid.Y, this.ToleranceDecimals);
            position["Z"] = Math.Round(mid.Z, this.ToleranceDecimals);
            Vector3d vector = pt1 - pt0;
            vector.Unitize();
            double angle = this.VectorRotation(vector);
            position["A"] = angle;

            this.CurrentLocation = new double[] { mid.X, mid.Y, mid.Z, angle };
            return position;
        }

        private bool ValidateInputTable(bool throwError = true)
        {
            // verify correct DataTable rows //
            string[] columns = new string[] {
                    "Type",
                    "Curve0",
                    "Curve1",
                    "Extend",
                };
            foreach (string col in columns)
            {
                if (!this.Parameters.Columns.Contains(col) && throwError)
                {
                    throw new MissingFieldException($"Input dataTable is missing column: {col}");
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        private string[] CreateGCode()
        {
            string previousCutType = string.Empty;
            var lines = new List<string>();
            lines.Add(this.NCHeader);

            for (int row = 0; row < this.GCode.Rows.Count; row++)
            {
                string line = string.Empty;
                string type = this.GCode.Rows[row]["Type"].ToString();

                if (type == "Retract" || previousCutType == string.Empty)
                {
                    line += this.RapidPrefix;
                }
                else
                {
                    line += this.CutPrefix;
                }

                // only add the changing lines, for better legibility //
                for (int i = 0; i < this.AxisPrefixes.Length; i++)
                {
                    if (row == 0 || ((double)this.GCode.Rows[row][i + 1] != (double)this.GCode.Rows[row - 1][i + 1]))
                    {
                        line += $"{this.AxisPrefixes[i]}{this.GCode.Rows[row][i + 1]} ";
                    }
                }

                if (previousCutType != "Drive" && type != "Retract")
                {
                    line += $" F{this.CuttingSpeed}";
                }

                lines.Add(line);
                previousCutType = type;
            }

            lines.Add(this.NCFooter);
            return lines.ToArray();
        }

        private bool WriteGcodeFile()
        {
            File.WriteAllLines(this.FilePath, this.CreateGCode());
            return true;
        }
    }
}
