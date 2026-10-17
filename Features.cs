namespace Aplat
{
    // Product switches: features that are built and kept working in code but not
    // offered in the interface. Flip one to bring the feature back everywhere.
    public static class Features
    {
        // Extruded 3-D text: the "3D text" tile / menu items, "Edit the
        // text…" and the TEXT section of the Object tab. Hidden by product choice;
        // scenes that already contain 3-D text still load and render as before.
        public static readonly bool Text3D = false;
    }
}
