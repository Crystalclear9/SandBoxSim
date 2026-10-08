using System.Text.Json;
using SandBoxSim.ConsoleApp.Online;
using SandBoxSim.Tests.Framework;
namespace SandBoxSim.Tests;
public sealed class OnlineApiTests
{
    private static JsonElement Call(StepApi api, string method, string path, string body="{}")
    {
        using var json=JsonDocument.Parse(body);
        return JsonSerializer.SerializeToElement(api.Dispatch(method,path,json.RootElement));
    }
    private static string Create(StepApi api)=>Call(api,"POST","/v1/sessions","{\"seed\":17,\"width\":24,\"height\":24,\"agents\":4}").GetProperty("sessionId").GetString()!;
    private static void Fails(StepApi api,string path,string body,int status)
    {
        try {Call(api,"POST",path,body);Assert.True(false,"Request should fail");}
        catch(ApiError e){Assert.Equal(status,e.Status);}
    }
    [Fact("Online steps are deterministic across independent sessions and observation is pure")]
    public void DeterministicStep()
    {
        var api=new StepApi();string a="/v1/sessions/"+Create(api),b="/v1/sessions/"+Create(api);
        string request="{\"requestId\":\"one\",\"expectedRevision\":0,\"ticks\":12}";
        var first=Call(api,"POST",a+"/step",request).GetProperty("observation");
        var second=Call(api,"POST",b+"/step",request).GetProperty("observation");
        Assert.Equal(first.GetProperty("digest").GetString(),second.GetProperty("digest").GetString());
        Assert.Equal(12L,first.GetProperty("tick").GetInt64());
        Assert.Equal(first.GetRawText(),Call(api,"GET",a).GetRawText());
        Call(api,"GET",a+"/map");Assert.Equal(first.GetRawText(),Call(api,"GET",a).GetRawText());
    }
    [Fact("Retries cannot double-step and stale revisions or reused IDs are rejected")]
    public void Idempotency()
    {
        var api=new StepApi();string path="/v1/sessions/"+Create(api);
        string request="{\"requestId\":\"one\",\"expectedRevision\":0,\"ticks\":3}";
        var first=Call(api,"POST",path+"/step",request);
        Assert.Equal(first.GetRawText(),Call(api,"POST",path+"/step",request).GetRawText());
        Assert.Equal(3L,Call(api,"GET",path).GetProperty("tick").GetInt64());
        Fails(api,path+"/step","{\"requestId\":\"two\",\"expectedRevision\":0,\"ticks\":3}",409);
        Fails(api,path+"/step","{\"requestId\":\"one\",\"expectedRevision\":1,\"ticks\":3}",409);
    }
    [Fact("Invalid action batches cause no partial mutation; schemas and limits reject invalid input")]
    public void AtomicValidation()
    {
        var api=new StepApi();string path="/v1/sessions/"+Create(api);var before=Call(api,"GET",path);
        Fails(api,path+"/step","{\"requestId\":\"bad\",\"expectedRevision\":0,\"ticks\":2,\"actions\":[{\"kind\":\"terrain\",\"x\":0,\"y\":0,\"terrain\":\"Road\"},{\"kind\":\"oops\",\"x\":0,\"y\":0}]}",400);
        Fails(api,path+"/step","{\"requestId\":\"bad\",\"expectedRevision\":0,\"ticks\":1001}",400);
        Fails(api,path+"/step","{\"requestId\":\"bad\",\"expectedRevision\":0,\"ticks\":1,\"ticks\":2}",400);
        Fails(api,path+"/step","{\"requestId\":\"bad\",\"expectedRevision\":\"zero\",\"ticks\":1}",409);
        Assert.Equal(before.GetRawText(),Call(api,"GET",path).GetRawText());
    }
    [Fact("Reset invalidates stale versions; sessions are isolated and can be deleted")]
    public void ResetAndDelete()
    {
        var api=new StepApi();string path="/v1/sessions/"+Create(api);
        var reset=Call(api,"POST",path+"/reset","{\"requestId\":\"reset\",\"expectedRevision\":0,\"seed\":19,\"width\":24,\"height\":24,\"agents\":0}");
        Assert.Equal(1L,reset.GetProperty("revision").GetInt64());Assert.Equal(0L,reset.GetProperty("tick").GetInt64());
        Fails(api,path+"/path","{\"expectedRevision\":0,\"queries\":[]}",409);
        Call(api,"DELETE",path);Fails(api,path+"/step","{}",404);
    }
}
