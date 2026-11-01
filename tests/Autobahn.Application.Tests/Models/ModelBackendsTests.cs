using FluentAssertions;
using Autobahn.Domain.Models;

namespace Autobahn.Application.Tests.Models;

public sealed class ModelBackendsTests
{
    [Fact]
    public void Normalize_OneUrlShape_BecomesOneDefaultBackend()
    {
        var model = ModelBackends.Normalize(new ModelConfig
        {
            Id = "m1",
            Url = " http://a:8000 ",
            MaxContextLength = 8192,
            UpstreamAuth = new UpstreamAuthConfig { SecretRef = "file:model:m1" },
        });

        model.Backends.Should().ContainSingle();
        var backend = model.Backends![0];
        backend.Name.Should().Be(ModelBackends.DefaultName);
        backend.Url.Should().Be("http://a:8000");
        backend.MaxContextLength.Should().Be(8192);
        backend.UpstreamAuth!.SecretRef.Should().Be("file:model:m1");
        backend.IsServing().Should().BeTrue();
        model.Url.Should().Be("http://a:8000");
    }

    [Fact]
    public void Normalize_List_RestatesTopLevelFromIt()
    {
        var model = ModelBackends.Normalize(new ModelConfig
        {
            Id = "m1",
            Url = "http://stale",
            MaxContextLength = 1,
            Backends =
            [
                new ModelBackendConfig { Name = "a", Url = "http://a", MaxContextLength = 262_144 },
                new ModelBackendConfig { Name = "b", Url = "http://b", MaxContextLength = 1_000_000 },
            ],
        });

        model.Url.Should().Be("http://a", "the first backend's address is what an old reader routes to");
        model.MaxContextLength.Should().Be(1_000_000, "the largest context is what a caller can actually send");
    }

    [Fact]
    public void Normalize_StoppedBackend_DoesNotCountTowardTheReportedContext()
    {
        var model = ModelBackends.Normalize(new ModelConfig
        {
            Id = "m1",
            Backends =
            [
                new ModelBackendConfig { Name = "a", Url = "http://a", MaxContextLength = 262_144 },
                new ModelBackendConfig
                {
                    Name = "b", Url = "http://b", MaxContextLength = 1_000_000, State = ModelRouteStates.Stopped,
                },
            ],
        });

        model.MaxContextLength.Should().Be(262_144);
    }

    [Fact]
    public void Normalize_EmptyList_IsAModelWithNothingToForwardTo()
    {
        var model = ModelBackends.Normalize(new ModelConfig { Id = "m1", Url = "http://gone", Backends = [] });

        model.Backends.Should().BeEmpty();
        model.Url.Should().BeEmpty();
        model.HasServingBackend().Should().BeFalse();
        model.IsInService().Should().BeFalse();
    }

    [Fact]
    public void Key_DefaultBackend_IsTheModelId_SoOneBackendModelsKeepTheirKey()
    {
        ModelBackends.Key("m1", ModelBackends.DefaultName).Should().Be("m1");
        ModelBackends.Key("m1", "a").Should().NotBe("m1");
        ModelBackends.SplitKey(ModelBackends.Key("org/m1", "a")).Should().Be(("org/m1", "a"));
        ModelBackends.SplitKey("org/m1").Should().Be(("org/m1", ModelBackends.DefaultName));
        ModelBackends.Display(ModelBackends.Key("m1", "a")).Should().Be("m1 / a");
        ModelBackends.Display("m1").Should().Be("m1");
    }

    [Theory]
    [InlineData("a", true)]
    [InlineData("gpu2-qwen.1", true)]
    [InlineData("", false)]
    [InlineData("-a", false)]
    [InlineData("a b", false)]
    [InlineData("a/b", false)]
    public void IsValidName_AcceptsOnlyPathSafeNames(string name, bool expected)
    {
        ModelBackends.IsValidName(name).Should().Be(expected);
    }

    [Fact]
    public void TryMergeUpdate_BodyWithList_ReplacesTheListButKeepsEachBackendsState()
    {
        var existing = Model(
            new ModelBackendConfig { Name = "a", Url = "http://a", State = ModelRouteStates.Stopped },
            new ModelBackendConfig { Name = "b", Url = "http://b" });
        var incoming = new ModelConfig
        {
            Id = "m1",
            Backends =
            [
                new ModelBackendConfig { Name = "a", Url = "http://a2" },
                new ModelBackendConfig { Name = "c", Url = "http://c", State = ModelRouteStates.Stopped },
            ],
        };

        ModelBackends.TryMergeUpdate(existing, incoming, out var backends, out var error).Should().BeTrue();

        error.Should().BeNull();
        backends.Select(b => (b.Name, b.Url, b.State)).Should().Equal(
            ("a", "http://a2", ModelRouteStates.Stopped),
            ("c", "http://c", ModelRouteStates.Stopped));
    }

    [Fact]
    public void TryMergeUpdate_OneUrlBody_ForAModelWithOneBackend_DescribesThatBackend()
    {
        var existing = Model(new ModelBackendConfig
        {
            Name = "gpu2", Url = "http://old", State = ModelRouteStates.Stopped,
            Labels = new Dictionary<string, string> { ["managed-by"] = "fleet" },
        });

        ModelBackends.TryMergeUpdate(
                existing,
                new ModelConfig { Id = "m1", Url = "http://new", MaxContextLength = 4096 },
                out var backends,
                out _)
            .Should().BeTrue();

        var only = backends.Should().ContainSingle().Subject;
        only.Name.Should().Be("gpu2");
        only.Url.Should().Be("http://new");
        only.MaxContextLength.Should().Be(4096);
        only.State.Should().Be(ModelRouteStates.Stopped);
        only.Labels.Should().ContainKey("managed-by");
    }

    [Fact]
    public void TryMergeUpdate_OneUrlBody_ForAModelWithSeveralBackends_KeepsTheList()
    {
        var existing = Model(
            new ModelBackendConfig { Name = "a", Url = "http://a" },
            new ModelBackendConfig { Name = "b", Url = "http://b" });

        // What the console sends when only an alias was edited: the address the model reports.
        ModelBackends.TryMergeUpdate(existing, new ModelConfig { Id = "m1", Url = "http://a" }, out var backends, out _)
            .Should().BeTrue();

        backends.Select(b => b.Url).Should().Equal("http://a", "http://b");
    }

    [Fact]
    public void TryMergeUpdate_OneUrlBodyWithADifferentUrl_ForAModelWithSeveralBackends_IsRefused()
    {
        var existing = Model(
            new ModelBackendConfig { Name = "a", Url = "http://a" },
            new ModelBackendConfig { Name = "b", Url = "http://b" });

        ModelBackends.TryMergeUpdate(existing, new ModelConfig { Id = "m1", Url = "http://elsewhere" }, out _, out var error)
            .Should().BeFalse();

        error.Should().Contain("2 backends");
    }

    [Fact]
    public void Validate_BackendList_RejectsDuplicateNamesBadUrlsAndForeignSecretRefs()
    {
        ModelConfigValidation.TryValidate(
                new ModelConfig
                {
                    Id = "m1",
                    Backends =
                    [
                        new ModelBackendConfig { Name = "a", Url = "http://a" },
                        new ModelBackendConfig { Name = "A", Url = "http://b" },
                    ],
                },
                out var duplicate)
            .Should().BeFalse();
        duplicate.Should().Contain("more than once");

        ModelConfigValidation.TryValidate(
                new ModelConfig { Id = "m1", Backends = [new ModelBackendConfig { Name = "a", Url = "ftp://a" }] },
                out var badUrl)
            .Should().BeFalse();
        badUrl.Should().Contain("backend 'a'").And.Contain("url");

        ModelConfigValidation.TryValidate(
                new ModelConfig
                {
                    Id = "m1",
                    Backends =
                    [
                        new ModelBackendConfig
                        {
                            Name = "a", Url = "http://a",
                            UpstreamAuth = new UpstreamAuthConfig { SecretRef = "file:model:m1:backend:b" },
                        },
                    ],
                },
                out var foreignRef)
            .Should().BeFalse();
        foreignRef.Should().Contain("secretRef");
    }

    [Fact]
    public void Validate_BackendList_AcceptsItsOwnSecretRefs_AndAnEmptyList()
    {
        ModelConfigValidation.TryValidate(
                new ModelConfig
                {
                    Id = "m1",
                    Backends =
                    [
                        new ModelBackendConfig
                        {
                            Name = ModelBackends.DefaultName, Url = "http://a",
                            UpstreamAuth = new UpstreamAuthConfig { SecretRef = UpstreamSecretRefs.ForModel("m1") },
                        },
                        new ModelBackendConfig
                        {
                            Name = "b", Url = "http://b",
                            UpstreamAuth = new UpstreamAuthConfig { SecretRef = UpstreamSecretRefs.ForBackend("m1", "b") },
                        },
                    ],
                },
                out var error)
            .Should().BeTrue(error);

        ModelConfigValidation.TryValidate(new ModelConfig { Id = "m1", Backends = [] }, out _).Should().BeTrue();
    }

    [Fact]
    public void SecretRefs_DefaultBackendKeepsTheModelsSlot_OthersGetTheirOwn()
    {
        UpstreamSecretRefs.ForBackend("m1", ModelBackends.DefaultName).Should().Be(UpstreamSecretRefs.ForModel("m1"));
        UpstreamSecretRefs.ForBackend("m1", "b").Should().Be("file:model:m1:backend:b");
        UpstreamSecretRefs.TryParseModelId("file:model:m1:backend:b", out var storeKey).Should().BeTrue();
        storeKey.Should().Be(UpstreamSecretRefs.StoreKeyForBackend("m1", "b"));
    }

    [Fact]
    public void TryValidateLabels_EnforcesTheLimits()
    {
        ModelBackends.TryValidateLabels(new Dictionary<string, string> { ["managed-by"] = "fleet" }, out _)
            .Should().BeTrue();
        ModelBackends.TryValidateLabels(
                Enumerable.Range(0, ModelBackends.MaxLabels + 1).ToDictionary(i => $"k{i}", _ => "v"),
                out var tooMany)
            .Should().BeFalse();
        tooMany.Should().Contain("at most");
        ModelBackends.TryValidateLabels(
                new Dictionary<string, string> { ["k"] = new string('x', ModelBackends.MaxLabelValueLength + 1) },
                out _)
            .Should().BeFalse();
    }

    private static ModelConfig Model(params ModelBackendConfig[] backends) =>
        ModelBackends.Normalize(new ModelConfig { Id = "m1", Backends = [.. backends] });
}
