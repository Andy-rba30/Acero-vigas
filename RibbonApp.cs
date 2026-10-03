using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Arba.Comun;
using Autodesk.Revit.UI;

namespace BeamRebar
{
    /// <summary>
    /// Entrada de la aplicacion de cinta para BeamRebar en Revit.
    /// Anade el boton "Vigas" al desplegable "Acero" del panel "Acero" en la pestana "ARBA"
    /// (la pestana, los paneles y el desplegable los gestiona ArbaRibbon del codigo comun ARBA-comun,
    /// compartido con los demas add-ins ARBA instalados). Solo el icono del boton es propio.
    /// </summary>
    public class RibbonApp : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                ArbaRibbon.Ensure(app);

                string assembly = Assembly.GetExecutingAssembly().Location;
                var data = new PushButtonData("ARBA_Acero_Vigas", "Vigas", assembly, typeof(ArmarVigaCommand).FullName)
                {
                    ToolTip = "Genera el armado de vigas rectangulares, en T o en L, de seccion constante o variable: barras corridas por capas, bastones y estribos",
                    LongDescription = "Selecciona una o varias vigas estructurales y pulsa el boton. Se abre la ventana " +
                                      "para elegir los tipos de barra de cada capa (superior e inferior), los bastones en los apoyos " +
                                      "o en el centro, la distribucion de estribos y los ganchos, con un esquema de la seccion y " +
                                      "del alzado. Si no hay nada seleccionado, el comando pide que elijas las vigas.",
                    LargeImage = IconVigas(32),
                    Image = IconVigas(16)
                };

                ArbaRibbon.AddAcero(app, data);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("ARBA", "No se pudo anadir el boton Vigas a la cinta: " + ex.Message +
                                "\nEl comando sigue disponible en Complementos > Herramientas externas.");
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

        /// <summary>Icono del boton Vigas: seccion rectangular con su estribo, barras corridas arriba y abajo y un baston.</summary>
        public static BitmapSource IconVigas(int size)
        {
            double s = size / 32.0;
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                var concrete = new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9));
                var edge = new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 1.2 * s);
                var stirrup = new Pen(new SolidColorBrush(Color.FromRgb(0x1F, 0x7A, 0x7A)), 1.6 * s) { LineJoin = PenLineJoin.Round };
                var bar = new SolidColorBrush(Color.FromRgb(0x8B, 0x2E, 0x2E));
                var baston = new SolidColorBrush(Color.FromRgb(0x7A, 0x3E, 0x9D));

                dc.DrawRectangle(concrete, edge, new System.Windows.Rect(7 * s, 2 * s, 18 * s, 28 * s));
                dc.DrawRectangle(null, stirrup, new System.Windows.Rect(10 * s, 5 * s, 12 * s, 22 * s));
                double rr = 1.8 * s;
                foreach (Point p in new[]
                {
                    new Point(10 * s, 5 * s), new Point(22 * s, 5 * s),
                    new Point(10 * s, 27 * s), new Point(16 * s, 27 * s), new Point(22 * s, 27 * s)
                })
                    dc.DrawEllipse(bar, null, p, rr, rr);
                dc.DrawEllipse(baston, null, new Point(16 * s, 5 * s), rr, rr);
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }
    }
}
