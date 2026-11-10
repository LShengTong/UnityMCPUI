using System;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
namespace UnityMcp.Editor
{
    internal sealed class UpdateTmpFontCharactersRequestHandler : IEditorRequestHandler
    {
        readonly EditorTaskQueue tasks;
        public UpdateTmpFontCharactersRequestHandler(EditorTaskQueue tasks) { this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks)); }
        public Task Handle(HttpListenerContext context) => HttpRequestUtility.Handle<UpdateTmpFontCharactersRequest>(context, tasks, Execute, Error, request => RequestProtocolValidation.Validate(request));
        static string Error(string id, string message) => JsonUtility.ToJson(new UpdateTmpFontCharactersReply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message });
        static string Execute(UpdateTmpFontCharactersRequest request)
        {
            try
            {
                HttpRequestUtility.ValidateEnvelope(request);
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play before modifying font assets");
                var font = TmpFontUtility.Update(request, out uint[] missing);
                return JsonUtility.ToJson(new UpdateTmpFontCharactersReply { id = request.id, instance_id = EditorInstanceInfo.InstanceId,
                    ok = true, asset_path = request.asset_path, guid = AssetDatabase.AssetPathToGUID(request.asset_path),
                    population_mode = font.atlasPopulationMode == TMPro.AtlasPopulationMode.Static ? "static" : "dynamic",
                    character_count = font.characterTable.Count, all_characters_present = missing.Length == 0,
                    missing_codepoints = TmpFontUtility.Missing(missing) });
            }
            catch (Exception error) { return Error(request.id, error.Message); }
        }
    }
}
