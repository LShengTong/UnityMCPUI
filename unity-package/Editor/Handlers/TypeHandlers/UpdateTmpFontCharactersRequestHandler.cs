using System;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
namespace UnityMcp.Editor
{
    internal sealed class UpdateTmpFontCharactersRequestHandler : IEditorRequestHandler
    {
        readonly EditorTaskQueue tasks;
        public UpdateTmpFontCharactersRequestHandler(EditorTaskQueue tasks) { this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks)); }
        public Task Handle(HttpListenerContext context) => HttpRequestUtility.Handle<UpdateTmpFontCharactersRequest>(context, tasks, Execute, Error, request => RequestProtocolValidation.Validate(request));
        static string Error(string id, string message) => JsonUtility.ToJson(new UpdateTmpFontCharactersReply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message });
        internal static string Execute(UpdateTmpFontCharactersRequest request)
        {
            try
            {
                HttpRequestUtility.ValidateEnvelope(request);
                var result = TmpFontService.UpdateTmpFontCharacters(request.asset_path, request.characters, request.mode);
            return JsonUtility.ToJson(new UpdateTmpFontCharactersReply {
                id = request.id, instance_id = EditorInstanceInfo.InstanceId,
                ok = result.ok,
                error = result.error,
                asset_path = result.asset_path,
                guid = result.guid,
                population_mode = result.population_mode,
                character_count = result.character_count,
                all_characters_present = result.all_characters_present,
                missing_codepoints = result.missing_codepoints
            });
            }
            catch (Exception error) { return Error(request.id, error.Message); }
        }
    }
}
