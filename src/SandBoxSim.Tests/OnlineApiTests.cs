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
    [Fact("Creation retries normalize defaults and property order, retain initial replies and work at capacity")]
    public void RecoverCreationAtCapacity()
    {
        var api=new StepApi();
        var created=Call(api,"POST","/v1/sessions","{\"requestId\":\"create-one\",\"seed\":17}");
        string path="/v1/sessions/"+created.GetProperty("sessionId").GetString();
        Call(api,"POST",path+"/step","{\"requestId\":\"step\",\"expectedRevision\":0,\"ticks\":1}");
        string reordered="{\"height\":48,\"agents\":24,\"width\":48,\"seed\":17,\"requestId\":\"create-one\"}";
        for(int i=1;i<StepApi.MaxSessions;i++)Create(api);
        Assert.Equal(created.GetRawText(),Call(api,"POST","/v1/sessions",reordered).GetRawText());
        Assert.Equal(1L,Call(api,"GET",path).GetProperty("tick").GetInt64());
        var listing=Call(api,"GET","/v1/sessions");
        Assert.Equal(StepApi.MaxSessions,listing.GetProperty("sessions").GetArrayLength());
        bool found=false;
        foreach(var entry in listing.GetProperty("sessions").EnumerateArray())
            if(entry.GetProperty("requestId").GetString()=="create-one")
            {found=true;Assert.Equal(1L,entry.GetProperty("observation").GetProperty("tick").GetInt64());}
        Assert.True(found);
        Fails(api,"/v1/sessions","{\"requestId\":\"create-one\",\"seed\":18}",409);
        Fails(api,"/v1/sessions","{\"requestId\":\"different\",\"seed\":17}",429);
    }
    [Fact("Deleted keyed sessions are tombstoned and bounded retirement never evicts a live creation key")]
    public void CreationTombstonesAreBounded()
    {
        var api=new StepApi();
        string active="{\"requestId\":\"active\",\"seed\":17,\"width\":16,\"height\":16,\"agents\":0}";
        var persistent=Call(api,"POST","/v1/sessions",active);
        string? firstId=null;
        for(int i=0;i<=StepApi.CreationTombstoneWindow;i++)
        {
            string request="{\"requestId\":\"retired-"+i+"\",\"seed\":17,\"width\":16,\"height\":16,\"agents\":0}";
            var created=Call(api,"POST","/v1/sessions",request);string id=created.GetProperty("sessionId").GetString()!;
            if(i==0)firstId=id;
            Call(api,"DELETE","/v1/sessions/"+id);
            Fails(api,"/v1/sessions",request,410);
        }
        Assert.Equal(persistent.GetRawText(),Call(api,"POST","/v1/sessions",active).GetRawText());
        Assert.Equal(1,Call(api,"GET","/v1/sessions").GetProperty("sessions").GetArrayLength());
        var reused=Call(api,"POST","/v1/sessions","{\"requestId\":\"retired-0\",\"seed\":17,\"width\":16,\"height\":16,\"agents\":0}");
        Assert.NotEqual(firstId,reused.GetProperty("sessionId").GetString());
        Assert.Equal(0L,reused.GetProperty("observation").GetProperty("tick").GetInt64());
    }
    [Fact("Invalid creation IDs and duplicate fields cannot allocate sessions")]
    public void ValidateCreationBeforeAllocation()
    {
        var api=new StepApi();
        foreach(string body in new[]{"{\"requestId\":\"\",\"seed\":17}","{\"requestId\":null,\"seed\":17}","{\"requestId\":\"x\",\"requestId\":\"x\",\"seed\":17}","{\"requestId\":\"x\",\"seed\":17,\"width\":1}"})
            Fails(api,"/v1/sessions",body,400);
        Assert.Equal(0,Call(api,"GET","/v1/sessions").GetProperty("sessions").GetArrayLength());
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
    [Fact("Online cached snapshots refresh after terrain mutation, ticks and reset")]
    public void SnapshotInvalidation()
    {
        var api=new StepApi();string path="/v1/sessions/"+Create(api);
        var before=Call(api,"GET",path+"/map");
        Call(api,"POST",path+"/step","{\"requestId\":\"terrain\",\"expectedRevision\":0,\"ticks\":0,\"actions\":[{\"kind\":\"terrain\",\"x\":0,\"y\":0,\"terrain\":\"Water\"}]}");
        var changed=Call(api,"GET",path+"/map");
        Assert.False(changed.GetProperty("tiles")[0].GetProperty("walkable").GetBoolean());
        Assert.Equal(1L,changed.GetProperty("revision").GetInt64());
        Assert.Equal(changed.GetRawText(),Call(api,"GET",path+"/map").GetRawText());
        Call(api,"POST",path+"/step","{\"requestId\":\"advance\",\"expectedRevision\":1,\"ticks\":2}");
        Assert.Equal(2L,Call(api,"GET",path).GetProperty("tick").GetInt64());
        Call(api,"POST",path+"/reset","{\"requestId\":\"new\",\"expectedRevision\":2,\"seed\":17,\"width\":24,\"height\":24,\"agents\":4}");
        var restored=Call(api,"GET",path+"/map");
        Assert.Equal(before.GetProperty("tiles").GetRawText(),restored.GetProperty("tiles").GetRawText());
        Assert.Equal(3L,restored.GetProperty("revision").GetInt64());
    }
    [Fact("Encoded HTTP snapshots reuse bytes only within the same world revision")]
    public void EncodedSnapshotInvalidation()
    {
        var api=new StepApi();string path="/v1/sessions/"+Create(api);using var json=JsonDocument.Parse("{}");
        var first=api.Encode("GET",path,json.RootElement);
        Assert.True(object.ReferenceEquals(first,api.Encode("GET",path+"/",json.RootElement)));
        var map=api.Encode("GET",path+"/map",json.RootElement);
        Call(api,"POST",path+"/step","{\"requestId\":\"step\",\"expectedRevision\":0,\"ticks\":1}");
        Assert.False(object.ReferenceEquals(first,api.Encode("GET",path,json.RootElement)));
        Assert.False(object.ReferenceEquals(map,api.Encode("GET",path+"/map",json.RootElement)));
        using var observation=JsonDocument.Parse(api.Encode("GET",path,json.RootElement));
        Assert.Equal(1L,observation.RootElement.GetProperty("tick").GetInt64());
    }

    [Fact("Idempotent replies cannot be replayed through another operation")]
    public void OperationBoundRetries()
    {
        var api=new StepApi();string path="/v1/sessions/"+Create(api);
        string step="{\"requestId\":\"one\",\"expectedRevision\":0,\"ticks\":2}";
        Call(api,"POST",path+"/step",step);
        Fails(api,path+"/reset",step,409);
        Assert.Equal(2L,Call(api,"GET",path).GetProperty("tick").GetInt64());
        string reset="{\"requestId\":\"reset\",\"expectedRevision\":1,\"seed\":17,\"width\":24,\"height\":24,\"agents\":4}";
        var result=Call(api,"POST",path+"/reset",reset);
        Assert.Equal(result.GetRawText(),Call(api,"POST",path+"/reset",reset).GetRawText());
        Fails(api,path+"/step",reset,409);
        Assert.Equal(1L,result.GetProperty("episode").GetProperty("epoch").GetInt64());
    }

    [Fact("Entity inspection is paginated, repeatable and does not mutate the world")]
    public void InspectEntities()
    {
        var api=new StepApi();string path="/v1/sessions/"+Create(api);
        var before=Call(api,"GET",path);
        string query="{\"expectedRevision\":0,\"kind\":\"agents\",\"limit\":2}";
        var first=Call(api,"POST",path+"/inspect",query);
        Assert.Equal(4,first.GetProperty("total").GetInt32());
        Assert.Equal(2,first.GetProperty("items").GetArrayLength());
        Assert.Equal(2,first.GetProperty("nextOffset").GetInt32());
        Assert.Equal(first.GetRawText(),Call(api,"POST",path+"/inspect",query).GetRawText());
        var second=Call(api,"POST",path+"/inspect","{\"expectedRevision\":0,\"kind\":\"agents\",\"offset\":2,\"limit\":2}");
        Assert.Equal(JsonValueKind.Null,second.GetProperty("nextOffset").ValueKind);
        var ids=new System.Collections.Generic.HashSet<string>();
        foreach(var page in new[]{first,second})foreach(var item in page.GetProperty("items").EnumerateArray())
        {
            Assert.True(ids.Add(item.GetProperty("entityId").GetString()!));
            Assert.True(item.GetProperty("generation").GetInt32()>=0);
            Assert.True(item.GetProperty("x").GetInt32()>=0);
        }
        Assert.Equal(4,ids.Count);
        var empty=Call(api,"POST",path+"/inspect","{\"expectedRevision\":0,\"kind\":\"buildings\"}");
        Assert.Equal(0,empty.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null,empty.GetProperty("nextOffset").ValueKind);
        Assert.Equal(before.GetRawText(),Call(api,"GET",path).GetRawText());
    }

    [Fact("Inspection rejects stale pages and reset epochs prevent entity identity aliasing")]
    public void InspectVersionsAndLimits()
    {
        var api=new StepApi();string path="/v1/sessions/"+Create(api);
        string query="{\"expectedRevision\":0,\"kind\":\"agents\"}";
        string oldId=Call(api,"POST",path+"/inspect",query).GetProperty("items")[0].GetProperty("entityId").GetString()!;
        foreach(string invalid in new[]{
            "{\"expectedRevision\":0,\"kind\":\"agents\",\"limit\":129}",
            "{\"expectedRevision\":0,\"kind\":\"agents\",\"limit\":0}",
            "{\"expectedRevision\":0,\"kind\":\"agents\",\"offset\":5}",
            "{\"expectedRevision\":0,\"kind\":\"other\"}",
            "{\"expectedRevision\":0,\"expectedRevision\":0,\"kind\":\"agents\"}"})
            Fails(api,path+"/inspect",invalid,400);
        Call(api,"POST",path+"/reset","{\"requestId\":\"reset\",\"expectedRevision\":0,\"seed\":17,\"width\":24,\"height\":24,\"agents\":4}");
        Fails(api,path+"/inspect",query,409);
        var page=Call(api,"POST",path+"/inspect","{\"expectedRevision\":1,\"kind\":\"agents\"}");
        Assert.Equal(1L,page.GetProperty("epoch").GetInt64());
        Assert.False(oldId==page.GetProperty("items")[0].GetProperty("entityId").GetString());
    }
}
