// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/OAuth/SingleFlowRegistry.cs
// PURPOSE:  A test registry that returns one flow for every provider id.
//           Lets the token-store and connection-service tests supply a
//           single flow substitute without constructing a real registry
//           with both flows.
// RELATION: Used by OAuthTokenStoreTests and ConsumerDriveOAuthTests.
// ============================================================================

using FileHarvester.Platform.Cloud.OAuth;

namespace FileHarvester.Tests.Cloud.OAuth;

/// <summary>A registry that returns one flow for any provider id.</summary>
internal sealed class SingleFlowRegistry : IOAuthFlowRegistry
{
    private readonly IOAuthFlow _flow;

    public SingleFlowRegistry(IOAuthFlow flow)
    {
        _flow = flow;
    }

    public IOAuthFlow Resolve(string providerId) => _flow;
}
