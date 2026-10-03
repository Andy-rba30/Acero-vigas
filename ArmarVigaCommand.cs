using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Arba.Comun;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace BeamRebar
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ArmarVigaCommand : IExternalCommand
    {
        /// <summary>Que hacer con las barras antiguas (particion "VIG-…" sin "ARBA - Origen") de un anfitrion.</summary>
        private enum LegacyChoice { MigrateOnly, MigrateAndRebuild, KeepAndBuild }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            AppConfig cfg;
            try { cfg = AppConfig.Load(); }
            catch (Exception ex)
            {
                message = "No se pudo leer config.json (" + AppConfig.ConfigPath() + "): " + ex.Message;
                return Result.Failed;
            }

            IList<Element> hosts;
            try { hosts = GetHosts(uidoc); }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

            if (hosts.Count == 0)
            {
                message = "No se selecciono ninguna viga estructural.";
                return Result.Cancelled;
            }

            var allTypes = RebarGenerator.AllBarTypes(doc);
            List<string> barTypes = allTypes.Select(b => b.Name).ToList();
            var diametersMm = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (RebarBarType bt in allTypes)
                diametersMm[bt.Name] = UnitUtils.ConvertFromInternalUnits(bt.BarNominalDiameter, UnitTypeId.Millimeters);
            if (barTypes.Count == 0)
            {
                message = "El proyecto no tiene ningun tipo de barra (RebarBarType). Carga una familia de armadura primero.";
                return Result.Failed;
            }
            var allHooks = RebarGenerator.AllHookTypes(doc);
            List<string> hookTypes = allHooks.Select(h => h.Name).ToList();
            // angulo de cada gancho (grados) para dibujarlo en el esquema de la seccion
            var hookAngles = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (RebarHookType h in allHooks)
            {
                double deg = 135;
                try { deg = Math.Round(h.HookAngle * 180 / Math.PI); } catch { }
                hookAngles[h.Name] = deg;
            }

            // --- 1. Analisis geometrico de cada elemento (solo lectura, sin transaccion) ---
            var items = hosts.Select(h => HostAnalysis.Analyze(doc, h, cfg)).ToList();

            // --- 2. Interfaz: el usuario revisa que se ha detectado y elige el armado ---
            var win = new RebarOptionsWindow(doc, cfg.Clone(), barTypes, diametersMm, hookTypes, hookAngles, items);
            try { new WindowInteropHelper(win).Owner = commandData.Application.MainWindowHandle; } catch { }
            bool? ok = win.ShowDialog();
            if (ok != true || win.Result == null) return Result.Cancelled;
            cfg = win.Result;

            // --- 3. Armadura ya existente (contrato ARBA): barras antiguas sin origen y barras propias del add-in ---
            // Las preguntas se hacen una sola vez, antes de abrir la transaccion, y valen para todas las vigas.
            var legacyHosts = new HashSet<HostAnalysis>(items.Where(i => i.CanBuild && HasLegacy(doc, i.Host)));
            var ownHosts = new HashSet<HostAnalysis>(items.Where(i => i.CanBuild && ArbaOrigin.Find(doc, ArbaContract.Vigas, i.Host).Count > 0));

            LegacyChoice legacy = LegacyChoice.KeepAndBuild;
            if (legacyHosts.Count > 0)
            {
                if (!AskLegacy(legacyHosts.Count, out legacy)) return Result.Cancelled;
                if (legacy == LegacyChoice.MigrateAndRebuild)
                    foreach (HostAnalysis i in legacyHosts) ownHosts.Add(i);   // tras migrar pasan a reconocerse como propias
                if (legacy == LegacyChoice.MigrateOnly)
                    foreach (HostAnalysis i in legacyHosts) ownHosts.Remove(i); // no se rearman: no hay nada que borrar
            }

            bool deleteOwn = false;
            if (ownHosts.Count > 0 && !AskDeleteOrKeep(ownHosts.Count, out deleteOwn)) return Result.Cancelled;

            // --- 4. Armado ---
            var log = new List<string>();
            var avisos = new List<string>();
            int total = 0, armed = 0, rejected = 0, migrated = 0, deletedSets = 0, deletedBars = 0;

            if (!ArbaPartition.TemplateFollowsContract(cfg.PartitionTemplate))
                avisos.Add("La plantilla de particion \"" + cfg.PartitionTemplate + "\" no cumple el contrato ARBA " + ArbaContract.Version +
                           " (tiene que empezar por \"{categoria} - {prefijo}-\"); el plugin de metrados no agrupara estas barras como VIGAS - VIG-...");

            using (Transaction tx = new Transaction(doc, "Armar vigas"))
            {
                tx.Start();

                // parametros compartidos del contrato (ARBA - Origen / Codigo, Metrado - Elemento) antes de la primera subtransaccion
                ArbaSharedParams.Ensure(doc, new[] { ArbaContract.Origen, ArbaContract.Codigo, ArbaContract.Elemento }, avisos);
                doc.Regenerate();

                // migracion de las barras antiguas (particion nueva + origen), sin crear ni borrar barras
                if (legacy != LegacyChoice.KeepAndBuild)
                {
                    foreach (HostAnalysis item in legacyHosts)
                    {
                        ArbaMigrationResult mr = ArbaMigration.MigrateHost(doc, item.Host, ArbaContract.Vigas);
                        migrated += mr.Migradas;
                        foreach (string a in mr.Avisos) if (!avisos.Contains(a)) avisos.Add(a);
                        if (legacy == LegacyChoice.MigrateOnly)
                            log.Add(item.Tag + "MIGRADO sin rearmar: " + mr.Migradas + " conjunto(s) con particion " +
                                    ArbaPartition.BuildFor(item.Host, ArbaContract.Vigas) + " y " + ArbaContract.Origen.Name + " = " +
                                    ArbaContract.Vigas.Origin + "; no se ha creado ninguna barra.");
                    }
                    doc.Regenerate();
                }

                foreach (HostAnalysis item in items)
                {
                    string tag = item.Tag;
                    if (legacy == LegacyChoice.MigrateOnly && legacyHosts.Contains(item)) continue;
                    if (!item.CanBuild)
                    {
                        rejected++;
                        log.Add(tag + "SIN ARMAR -> " + item.Detail(cfg));
                        continue;
                    }

                    // Cada elemento se arma dentro de una subtransaccion. Si cualquier barra
                    // queda fuera del hormigon (red de seguridad), se deshace TODO lo creado
                    // para ese elemento (y el borrado de su armadura anterior): o se arma
                    // entero y bien, o no se arma y se conserva lo que habia.
                    using (SubTransaction sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        BuildResult res = null;
                        string error = null;
                        int delSets = 0, delBars = 0;
                        try
                        {
                            if (deleteOwn && ownHosts.Contains(item))
                            {
                                delSets = ArbaOrigin.Delete(doc, ArbaContract.Vigas, item.Host, out delBars);
                                if (delSets > 0) doc.Regenerate();
                            }
                            res = RebarGenerator.Build(doc, item, cfg);
                            if (res.Safe && res.Created.Count > 0)
                            {
                                doc.Regenerate();
                                RebarGenerator.VerifyCreated(doc, item.Section, res);
                            }
                        }
                        catch (Exception ex)
                        {
                            error = ex.Message;
                        }

                        bool keep = error == null && res != null && res.Safe;
                        string desc = item.Detail(cfg);
                        if (keep)
                        {
                            sub.Commit();
                            armed++;
                            total += res.Created.Count;
                            deletedSets += delSets;
                            deletedBars += delBars;
                            string line = tag + desc + "  ->  " + res.Summary + " (" + res.Created.Count + " conjuntos)";
                            if (delSets > 0)
                                line += "  [borrados " + delSets + " conjuntos anteriores del add-in (" + delBars + " barras)]";
                            if (res.Failed.Count > 0)
                                line += "  INCOMPLETO, no se pudieron crear: " + string.Join(" | ", res.Failed);
                            if (res.Warnings.Count > 0)
                                line += "  AVISOS: " + string.Join(" | ", res.Warnings);
                            log.Add(line);
                        }
                        else
                        {
                            sub.RollBack();
                            rejected++;
                            string kept = delSets > 0 ? " Se conserva la armadura anterior del add-in." : "";
                            if (error != null)
                                log.Add(tag + "SIN ARMAR -> ERROR: " + error + ". Se ha deshecho todo lo creado para este elemento." + kept);
                            else
                                log.Add(tag + "SIN ARMAR -> " + desc + ": barras fuera del hormigon, se ha deshecho todo el " +
                                        "elemento (" + res.Rejected.Count + "): " + string.Join(" | ", res.Rejected) + kept);
                        }
                    }
                }
                tx.Commit();
            }

            var td = new TaskDialog("Armado de vigas")
            {
                MainInstruction = total + " conjuntos de armadura creados en " + armed + " de " + hosts.Count + " elemento(s)." +
                                  (deletedSets > 0 ? " Borrados " + deletedSets + " conjuntos anteriores (" + deletedBars + " barras)." : "") +
                                  (migrated > 0 ? " Migrados " + migrated + " conjuntos antiguos al contrato." : ""),
                MainContent = string.Join(Environment.NewLine, log),
                FooterText = "Particion " + ArbaPartition.FilterPrefix(ArbaContract.CatVigas, ArbaContract.Vigas.Prefix) + "{marca}, " +
                             ArbaContract.Origen.Name + " = " + ArbaContract.Vigas.Origin + ". Contrato ARBA-comun " + ArbaContract.Version + "."
            };
            if (avisos.Count > 0)
                td.ExpandedContent = "Avisos:" + Environment.NewLine + string.Join(Environment.NewLine, avisos.Select(a => "  - " + a));
            if (rejected > 0)
            {
                td.MainInstruction += Environment.NewLine + "ATENCION: " + rejected +
                                      " elemento(s) SIN ARMAR (ver detalle). No se ha creado ninguna barra en ellos.";
                td.MainIcon = TaskDialogIcon.TaskDialogIconWarning;
            }
            td.Show();
            return Result.Succeeded;
        }

        /// <summary>True si el anfitrion tiene barras de este add-in anteriores al contrato ("VIG-…" sin "ARBA - Origen").</summary>
        private static bool HasLegacy(Document doc, Element host)
        {
            try { return ArbaMigration.HasLegacy(doc, host, ArbaContract.Vigas); }
            catch (Exception) { return false; }
        }

        /// <summary>Pregunta que hacer con las barras antiguas. False si el usuario cancela.</summary>
        private static bool AskLegacy(int hostCount, out LegacyChoice choice)
        {
            choice = LegacyChoice.KeepAndBuild;
            var td = new TaskDialog("Armar vigas: barras anteriores al contrato ARBA")
            {
                MainInstruction = hostCount + " viga(s) ya tienen barras de este add-in con la particion antigua (VIG-…) y sin \"" +
                                  ArbaContract.Origen.Name + "\".",
                MainContent = "Migrarlas las convierte a la particion del contrato (" + ArbaContract.CatVigas + " - " + ArbaContract.Vigas.Prefix +
                              "-marca) y rellena \"" + ArbaContract.Origen.Name + "\" y \"" + ArbaContract.Elemento.Name +
                              "\" sin crear ni borrar ninguna barra; asi el add-in las reconoce como suyas y el plugin de metrados las agrupa. " +
                              "Ctrl+Z deshace todo.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.CommandLink1,
                FooterText = "Contrato ARBA-comun " + ArbaContract.Version
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Migrar sin rearmar",
                              "Solo actualiza particion y origen de las barras que ya hay. En esas vigas no se crea nada nuevo.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Migrar y rearmar",
                              "Migra las barras y despues pregunta si borrarlas y rearmar o conservarlas.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Dejar las barras antiguas como estan y armar encima",
                              "No se migran ni se borran: la viga quedara con la armadura antigua y la nueva (duplicada).");
            switch (td.Show())
            {
                case TaskDialogResult.CommandLink1: choice = LegacyChoice.MigrateOnly; return true;
                case TaskDialogResult.CommandLink2: choice = LegacyChoice.MigrateAndRebuild; return true;
                case TaskDialogResult.CommandLink3: choice = LegacyChoice.KeepAndBuild; return true;
                default: return false;
            }
        }

        /// <summary>Pregunta si borrar la armadura del add-in y rearmar o conservarla. False si el usuario cancela.</summary>
        private static bool AskDeleteOrKeep(int hostCount, out bool delete)
        {
            delete = false;
            var td = new TaskDialog("Armar vigas: armadura ya existente")
            {
                MainInstruction = hostCount + " viga(s) ya tienen armadura creada por este add-in (\"" + ArbaContract.Origen.Name + "\" = " +
                                  ArbaContract.Vigas.Origin + ").",
                MainContent = "Se reconocen por \"" + ArbaContract.Origen.Name + "\", no por la particion ni por comentarios. " +
                              "Las barras de otros add-ins o modeladas a mano no se tocan.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.CommandLink1,
                FooterText = "Contrato ARBA-comun " + ArbaContract.Version
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Borrar la armadura del add-in y rearmar",
                              "Se borran los conjuntos de este add-in en cada viga antes de armarla. Si una viga no se puede armar, se conserva lo que tenia.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Conservar y armar encima",
                              "La armadura anterior se queda y se crea la nueva ademas (duplicada).");
            switch (td.Show())
            {
                case TaskDialogResult.CommandLink1: delete = true; return true;
                case TaskDialogResult.CommandLink2: delete = false; return true;
                default: return false;
            }
        }

        private static IList<Element> GetHosts(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            var sel = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .Where(IsCandidate)
                .ToList();
            if (sel.Count > 0) return sel;

            IList<Reference> refs = uidoc.Selection.PickObjects(
                ObjectType.Element, new HostFilter(),
                "Selecciona las vigas estructurales a armar y pulsa Finalizar");
            return refs.Select(r => doc.GetElement(r)).ToList();
        }

        private static bool IsCandidate(Element e)
        {
            if (e == null || e.Category == null) return false;
            return e.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;
        }

        private class HostFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => IsCandidate(e);
            public bool AllowReference(Reference r, XYZ p) => false;
        }
    }
}
