namespace WirecutterGH
{
    using System;
    using System.Drawing;
    using Grasshopper.Kernel;

    /// <summary>
    /// Grasshopper plugin info.
    /// </summary>
    public class WirecutterGHInfo : GH_AssemblyInfo
    {
        /// <summary>
        /// Gets the name of the plugin.
        /// </summary>
        public override string Name
        {
            get
            {
                return "WirecutterGH";
            }
        }

        /// <summary>
        /// Gets the icon used for the grasshopper component.
        /// </summary>
        public override Bitmap Icon
        {
            get
            {
                // Return a 24x24 pixel bitmap to represent this GHA library.
                return null;
            }
        }

        /// <summary>
        /// Gets the description used for the plugin.
        /// </summary>
        public override string Description
        {
            get
            {
                // Return a short string describing the purpose of this GHA library.
                return "Components for creating g-code for ruled surface wire cutting.";
            }
        }

        /// <summary>
        /// Gets the identifier for the component.
        /// </summary>
        public override Guid Id
        {
            get
            {
                return new Guid("30b27028-5d3e-4d53-bec4-0bd6adbe07cd");
            }
        }

        /// <summary>
        /// Gets the name of the plugin author.
        /// </summary>
        public override string AuthorName
        {
            get
            {
                // Return a string identifying you or your company.
                return "Peter Schmidt";
            }
        }

        /// <summary>
        /// Gets the contact info for the plugin pulblisher.
        /// </summary>
        public override string AuthorContact
        {
            get
            {
                // Return a string representing your preferred contact details.
                return "www.github.com/5chmidt";
            }
        }
    }
}
