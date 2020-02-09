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
            this.Doc = doc;
            this.Parameters = dt;
            this.Xform = Rhino.Geometry.Transform.PlaneToPlane(plane, this.CutPlane);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Wirecutter"/> class.
        /// </summary>
        public Wirecutter()
        {
            this.DefaultSetup();
        }

        /// <summary>
        /// Gets or sets the speed for cutting toolpaths (inches per minute).
        /// </summary>
        public int CuttingSpeed { get; set; }

        /// <summary>
        /// Gets or sets a message if there is an error generating a toolpath, to be viewed by the end user.
        /// </summary>
        public string ErrorMessage { get; set; }

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
        /// Gets or sets the minumum accurate measurement default: 0.001".
        /// </summary>
        public double Tolerance { get; set; }

        /// <summary>
        /// Gets or sets the number of decimal places to calculate tolerance.
        /// </summary>
        public int ToleranceDecimals { get; set; }

        private string[] AxisPrefixes { get; set; }

        private double[] CurrentLocation { get; set; }

        private Plane CutPlane { get; set; }

        private RhinoDoc Doc { get; set; }

        private string RapidPrefix { get; set; }

        private string CutPrefix { get; set; }

        private DataTable GCode { get; set; }

        private string FeedPrefix { get; set; }

        private DataTable Parameters { get; set; }

        private Transform Xform { get; set; }

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
                            curve.Transform(this.Xform);
                            var polyline = curve.ToPolyline(this.Tolerance, this.Tolerance, this.Tolerance, 1000).ToPolyline();
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
                            if (gcode["X"] == lastRow["X"]
                            && gcode["Y"] == lastRow["Y"]
                            && gcode["Z"] == lastRow["Z"]
                            && gcode["A"] == lastRow["A"])
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

            if (!this.CutPlane.Normal.IsPerpendicularTo(vector))
            {
                vector = (Vector3d)this.CutPlane.ClosestPoint(new Point3d(vector.X, vector.Y, vector.Z));
            }

            var hAngle = RhinoMath.ToDegrees(Vector3d.VectorAngle(vector, horizontal));
            var vAngle = RhinoMath.ToDegrees(Vector3d.VectorAngle(vector, vertical));

            double angle;
            if (Math.Abs(hAngle + vAngle - 90) <= this.Tolerance * 360)
            {
                // Quadrent I //
                angle = hAngle;
            }
            else if (Math.Abs(hAngle - vAngle - 90) <= this.Tolerance *360)
            {
                // Quadrend II //
                angle = 90 + vAngle;
            }
            else if (Math.Abs(hAngle + vAngle - 270) <= this.Tolerance * 360)
            {
                // Quadrend III //
                angle = 90 + vAngle;
            }
            else if (Math.Abs(vAngle - hAngle - 90) <= this.Tolerance * 360)
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
            double angle = this.VectorRotation(pt1 - pt0);
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
