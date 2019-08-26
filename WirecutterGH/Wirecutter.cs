// Developed by Boston Valley Terra Cotta.
namespace WirecutterGH
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.IO;
    using Rhino;
    using Rhino.Geometry;

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
            this.Doc = doc;
            this.Parameters = dt;
            this.NCHeader = "G54";
            this.NCFooter = "M30";

            this.CurrentLocation = new double[] { -offset, 0, 0, 0 };
            this.AxisPrefixes = new string[] { "X ", "Y ", "Z ", "Q1="};
            this.FeedPrefix = "F";
            this.CutPrefix = "G01   ";
            this.CuttingSpeed = 200;
            this.RapidPrefix = "G00   ";
            this.CutPlane = Rhino.Geometry.Plane.WorldYZ;
            this.Xform = Rhino.Geometry.Transform.PlaneToPlane(plane, this.CutPlane);
            this.Tolerance = 0.01;
            this.ToleranceDecimals = 3;

            // build toolpath datatable //
            this.GCode = new DataTable("GCode");
            this.GCode.Columns.Add("Type", typeof(string));
            this.GCode.Columns.Add("X", typeof(double));
            this.GCode.Columns.Add("Y", typeof(double));
            this.GCode.Columns.Add("Z", typeof(double));
            this.GCode.Columns.Add("A", typeof(double));
        }

        public Wirecutter()
        {
            this.CutPlane = Rhino.Geometry.Plane.WorldYZ;
            this.Tolerance = 0.001;
            this.ToleranceDecimals = 3;
            this.CurrentLocation = new double[] { 0, 0, 0, 1 };

        }

        /// <summary>
        /// Gets or sets the speed for cutting toolpaths (inches per minute).
        /// </summary>
        public int CuttingSpeed { get; set; }

        /// <summary>
        /// Gets or sets the machine code header that will be applied before all movement instructions.
        /// </summary>
        public string NCHeader { get; set; }

        /// <summary>
        /// Gets or sets the machine code footer that will be applied following all movement instructions.
        /// </summary>
        public string NCFooter { get; set; }

        /// <summary>
        /// Gets or sets a message if there is an error generating a toolpath, to be viewed by the end user.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Gets or sets the location of the NC file that is in progress.
        /// </summary>
        public string FilePath { get; set; }

        private string[] AxisPrefixes { get; set; }

        private double[] CurrentLocation { get; set; }

        private Plane CutPlane { get; set; }

        private RhinoDoc Doc { get; set; }

        private string RapidPrefix { get; set; }

        private string CutPrefix { get; set; }
        
        private DataTable GCode { get; set; }

        private string FeedPrefix { get; set; }

        private DataTable Parameters { get; set; }

        /// <summary>
        /// Gets or sets the minumum accurate measurement default: 0.001".
        /// </summary>
        public double Tolerance { get; set; }

        /// <summary>
        /// Gets or sets the number of decimal places to calculate tolerance.
        /// </summary>
        public int ToleranceDecimals { get; set; }

        /// <summary>
        /// Generate a list of curves that show the toolpath movement.
        /// </summary>
        /// <param name="onOrigin">If true generate the toolpaths at world Origin.</param>
        /// <returns></returns>
        public Curve[] CutCurves(bool onOrigin)
        {
            List<Curve> curves = new List<Curve>();
            foreach (DataRow row in this.GCode.Rows)
            {
                double x = (double)row["X"];
                double y = (double)row["Y"];
                double z = (double)row["Z"];
                double angle = (double)row["A"];

                Transform xform = Transform.Rotation(RhinoMath.ToRadians(angle), this.CutPlane.Normal, new Point3d(x, y, z));
                Point3d pt0 = new Point3d(x, y, z) + (this.CutPlane.XAxis * 25);
                pt0.Transform(xform);

                Point3d pt1 = new Point3d(x, y, z) - (this.CutPlane.XAxis * 25);
                pt0.Transform(xform);

                Curve crv = new Line(pt0, pt1).ToNurbsCurve();
                if (!onOrigin && this.Xform.TryGetInverse(out xform))
                {
                    crv.Transform(xform);
                }

                curves.Add(crv);
            }

            return curves.ToArray();
        }

        /// <summary>
        /// Method to combine parameters of the toolpath class to write a simple text file containing machine instructions.
        /// </summary>
        /// <param name="path">string - the filepath where the gcode will be output.</param>
        /// <returns>bool - On success true, otherwise false.</returns>
        public bool ToolpathCurves(string path)
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
                            var polyline = curve.ToPolyline(this.Tolerance, this.Tolerance, this.Tolerance, 100).ToPolyline();
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
                    while (segments[0] < polylines[0].SegmentCount && segments[1] < polylines[1].SegmentCount)
                    {
                        Point3d[] pts = new Point3d[] { polylines[0][segments[0]], polylines[1][segments[1]] };
                        double dist0 = this.CutPlane.DistanceTo(pts[0]);
                        double dist1 = this.CutPlane.DistanceTo(pts[1]);

                        if ((dist0 - dist1) < this.Tolerance)
                        {
                            segments[0] += 1;
                            segments[1] += 1;
                        }
                        else if (dist0 < dist1)
                        {
                            var plane = new Plane(pts[0], this.CutPlane.Normal);
                            var intersects = Rhino.Geometry.Intersect.Intersection.CurvePlane(curves[1], plane, this.Tolerance);
                            if (intersects == null)
                            {
                                pts[1] = this.CutPlane.ClosestPoint(pts[1]);
                            }

                            segments[0] += 1;
                        }
                        else
                        {
                            var plane = new Plane(pts[1], this.CutPlane.Normal);
                            var intersects = Rhino.Geometry.Intersect.Intersection.CurvePlane(curves[0], plane, this.Tolerance);
                            if (intersects == null)
                            {
                                pts[0] = this.CutPlane.ClosestPoint(pts[0]);
                            }

                            segments[1] += 1;
                        }

                        // assign new U values //
                        for (int i = 0; i < u.Length; i++)
                        {
                            curves[i].ClosestPoint(pts[i], out u[i]);
                        }

                        DataRow gcode = this.CalculatePostion(pts[0], pts[1]);
                        gcode["Type"] = type;
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
        public bool ToolpathLines(Line[] lines, string[] types)
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

        private Transform Xform { get; set; }

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

        private bool WriteGcodeFile()
        {
            string previousCutType = string.Empty;

            using (StreamWriter writetext = new StreamWriter(this.FilePath))
            {
                writetext.WriteLine(this.NCHeader);
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
                        else
                        {
                            Console.WriteLine();
                        }
                    }

                    if (previousCutType != "Drive" && type != "Retract")
                    {
                        line += $" F{this.CuttingSpeed}";
                    }

                    writetext.WriteLine(line);
                    previousCutType = type;
                }

                writetext.WriteLine(this.NCFooter);
            }

            return true;
        }
    }
}
