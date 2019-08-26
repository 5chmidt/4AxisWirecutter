﻿using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace WirecutterGH
{
    public class WirecutterGHInfo : GH_AssemblyInfo
    {
        public override string Name
        {
            get
            {
                return "WirecutterGH";
            }
        }
        public override Bitmap Icon
        {
            get
            {
                //Return a 24x24 pixel bitmap to represent this GHA library.
                return null;
            }
        }
        public override string Description
        {
            get
            {
                //Return a short string describing the purpose of this GHA library.
                return "";
            }
        }
        public override Guid Id
        {
            get
            {
                return new Guid("30b27028-5d3e-4d53-bec4-0bd6adbe07cd");
            }
        }

        public override string AuthorName
        {
            get
            {
                //Return a string identifying you or your company.
                return "";
            }
        }
        public override string AuthorContact
        {
            get
            {
                //Return a string representing your preferred contact details.
                return "";
            }
        }
    }
}