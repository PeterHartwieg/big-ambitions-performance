using System;
using System.Collections.Generic;
using System.IO;

namespace CityPerformance
{
    public static class SaveFileCommit
    {
        // Each replacement is atomic on the same filesystem. The pair is not a filesystem
        // transaction: a process crash between replacements can leave stale display metadata.
        // Retain both previous files so that the preceding complete save remains recoverable.
        public static void Publish(string dataTemp, string metaTemp, string destination, Action<string> checkpoint = null,
            Action<string, string> replace = null)
        {
            string meta = destination + ".meta", backup = destination + ".cityperformance-previous";
            bool hadData = File.Exists(destination), hadMeta = File.Exists(meta), dataAttempted = false, metaAttempted = false;
            bool dataCreated = false, metaCreated = false;
            try
            {
                // Establish a complete recovery pair before either replacement. Windows
                // ReplaceFile can move the old destination and then throw; its return
                // value alone is not proof that the destination remained intact.
                if (hadData || hadMeta)
                {
                    if (hadData) File.Copy(destination, backup, true); else File.Delete(backup);
                    if (hadMeta) File.Copy(meta, backup + "-metadata", true); else File.Delete(backup + "-metadata");
                }
                replace = replace ?? ((source, target) => File.Replace(source, target, null));
                checkpoint?.Invoke("before-data");
                dataAttempted = true;
                if (hadData) replace(dataTemp, destination); else { File.Move(dataTemp, destination); dataCreated = true; }
                checkpoint?.Invoke("after-data");
                metaAttempted = true;
                if (hadMeta) replace(metaTemp, meta); else { File.Move(metaTemp, meta); metaCreated = true; }
                checkpoint?.Invoke("after-meta");
            }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                // A failing replace may already have changed its target. Restore each
                // attempted target independently, even if the other restoration fails.
                if (metaAttempted) try
                {
                    if (hadMeta) Restore(backup + "-metadata", meta); else if (metaCreated) File.Delete(meta);
                }
                catch (Exception rollback) { errors.Add(rollback); }
                if (dataAttempted) try
                {
                    if (hadData) Restore(backup, destination); else if (dataCreated) File.Delete(destination);
                }
                catch (Exception rollback) { errors.Add(rollback); }
                if (errors.Count > 1) throw new AggregateException("Save publication and rollback failed; previous files retained at " + backup, errors);
                throw;
            }
        }
        private static void Restore(string backup, string destination)
        {
            string staging = destination + ".cityperformance-rollback";
            File.Copy(backup, staging, true);
            if (File.Exists(destination)) File.Replace(staging, destination, null); else File.Move(staging, destination);
        }
    }
}
