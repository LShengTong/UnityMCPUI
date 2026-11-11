using System;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
namespace UnityMcp.Editor
{
    internal sealed class CreateTmpFontRequestHandler : IEditorRequestHandler
    {
        readonly EditorTaskQueue tasks;
        public CreateTmpFontRequestHandler(EditorTaskQueue tasks) { this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks)); }
        public Task Handle(HttpListenerContext context) => HttpRequestUtility.Handle<CreateTmpFontRequest>(context, tasks, Execute, Error, request => RequestProtocolValidation.Validate(request));
        static string Error(string id, string message) => JsonUtility.ToJson(new CreateTmpFontReply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message });
        internal static string Execute(CreateTmpFontRequest request)
        {
            try
            {
                HttpRequestUtility.ValidateEnvelope(request);
                var result = TmpFontService.CreateTmpFont(request.source_font_path, request.asset_path, request.characters, request.population_mode, request.sampling_point_size, request.atlas_size, request.padding);
            return JsonUtility.ToJson(new CreateTmpFontReply {
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
