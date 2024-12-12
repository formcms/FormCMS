using FluentCMS.Test.Util;
using FluentCMS.Utils.ApiClient;
using FluentCMS.Utils.ResultExt;

namespace FluentCMS.Blog.Tests;
public class EntityApiTest 
{
    private const string Post = "entity_api_test_post";
    private const string Title = "title";

    private readonly AccountApiClient _accountApiClient;
    private readonly EntityApiClient _entityApiClient;
    private readonly SchemaApiClient _schemaApiClient;

    public EntityApiTest()
    {
        WebAppClient<Program> webAppClient = new();
        _entityApiClient = new EntityApiClient(webAppClient.GetHttpClient());
        _schemaApiClient = new SchemaApiClient(webAppClient.GetHttpClient());
        _accountApiClient = new AccountApiClient(webAppClient.GetHttpClient());
    }


    [Fact]
    public async Task EntityRetrieve()
    {
        var post = Post + Guid.NewGuid().ToString("N");

        await _accountApiClient.EnsureLogin();
        (await _schemaApiClient.EnsureSimpleEntity(post, Title)).AssertSuccess();
        for (var i = 0; i < 5; i++)
        {
            (await _entityApiClient.AddSimpleData(post, Title, $"student{i}")).AssertSuccess();
        }

        (await _entityApiClient.AddSimpleData(post, Title, "good-student")).AssertSuccess();
        (await _entityApiClient.AddSimpleData(post, Title, "good-student")).AssertSuccess();

        //get first page
        Assert.Equal(5,(await _entityApiClient.GetEntityList(post, 0, 5)).AssertSuccess().Items.Length);
        //get last page
        var res = (await _entityApiClient.GetEntityList(post, 5, 5)).AssertSuccess();
        Assert.Equal(2,res.Items.Length);
    }
}