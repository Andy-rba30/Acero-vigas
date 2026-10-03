using System;
using System.Collections.Generic;
using Arba.Comun;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace BeamRebar
{
    /// <summary>
    /// Resultado del analisis de un elemento seleccionado, antes de armar nada: el perfil
    /// deducido o el motivo del rechazo, mas las elecciones por elemento hechas en la
    /// ventana (distribucion de estribos y barras por capa propias).
    /// </summary>
    public sealed class HostAnalysis
    {
        public Element Host;
        public string Tag;
        public string Mark = "", TypeName = "", FamilyName = "";

        public BeamSection Section;
        public string Error;

        /// <summary>Distribucion de estribos propia de este elemento ("" = la de la configuracion).</summary>
        public string DistributionOverride = "";
        /// <summary>Barras por capa propias de esta viga: clave "S1", "S2"... (superior) e "I1"... (inferior).</summary>
        public Dictionary<string, int> LayerOverrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Tipo de barra asignado a mano a barras concretas de esta viga (clave de PlanBar -> nombre del tipo).</summary>
        public Dictionary<string, string> BarTypeOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string Key(bool top, int layer) => (top ? "S" : "I") + layer;

        /// <summary>Barras propias de una capa, o -1 si usa el general.</summary>
        public int Own(bool top, int layer) => LayerOverrides.TryGetValue(Key(top, layer), out int n) ? n : -1;

        public void SetOwn(bool top, int layer, int count)
        {
            if (count < 0) LayerOverrides.Remove(Key(top, layer));
            else LayerOverrides[Key(top, layer)] = count;
        }

        /// <summary>Pares de barras laterales propios de esta viga, o -1 si usa el general.</summary>
        public int OwnSide() => LayerOverrides.TryGetValue("L", out int n) ? n : -1;
        public void SetOwnSide(int pairs) { if (pairs < 0) LayerOverrides.Remove("L"); else LayerOverrides["L"] = pairs; }

        public bool CanBuild => Error == null && Section != null;

        public string Kind => Error != null ? "SIN ARMAR" : "Viga " + Section.KindName;

        public string Detail(AppConfig cfg) => Error ?? Section.Describe();

        public string Distribution(AppConfig cfg) =>
            string.IsNullOrWhiteSpace(DistributionOverride) ? cfg.Stirrups.Distribution : DistributionOverride;

        /// <summary>
        /// Particion del contrato ARBA para un conjunto de esta viga: la categoria la deduce el codigo comun del
        /// anfitrion (VIGAS para armazon estructural), el prefijo es VIG y la cara (superior / inferior / lateral /
        /// baston / estribo) va en {codigo}; con la plantilla por defecto queda "VIGAS - VIG-V-101".
        /// </summary>
        public string Partition(AppConfig cfg, string setName, string face)
        {
            return ArbaPartition.BuildFor(Host, ArbaContract.Vigas, cfg.PartitionTemplate, new PartitionName.Source
            {
                Mark = Mark, TypeName = TypeName, FamilyName = FamilyName,
                SetName = setName, Code = face
            });
        }

        public static HostAnalysis Analyze(Document doc, Element host, AppConfig cfg)
        {
            var a = new HostAnalysis { Host = host, Tag = "[" + host.Id + " " + host.Name + "] " };
            try
            {
                a.Mark = host.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "";
                a.TypeName = BeamSection.TypeNameOf(doc, host) ?? "";
                if (host is FamilyInstance fi) a.FamilyName = fi.Symbol?.Family?.Name ?? "";
                else a.FamilyName = host.Category?.Name ?? "";
            }
            catch { }
            a.Reanalyze(doc, cfg);
            return a;
        }

        /// <summary>Vuelve a leer la geometria (por ejemplo al cambiar como tratar la geometria unida); conserva las elecciones propias.</summary>
        public void Reanalyze(Document doc, AppConfig cfg)
        {
            Section = null;
            Error = null;
            try
            {
                RebarHostData hd = RebarHostData.GetRebarHostData(Host);
                if (hd == null || !hd.IsValidHost())
                {
                    Error = "no admite armadura. Revisa que el material sea hormigon y que sea una viga estructural.";
                    return;
                }
                Section = BeamSection.Probe(doc, Host, cfg);
                if (Section == null)
                    Error = "RECHAZADO, " + (BeamSection.LastError ?? "no se pudo deducir la seccion (motivo desconocido)") +
                            ". No se ha creado ninguna barra.";
            }
            catch (Exception ex)
            {
                Section = null;
                Error = "ERROR: " + ex.Message;
            }
        }
    }
}
