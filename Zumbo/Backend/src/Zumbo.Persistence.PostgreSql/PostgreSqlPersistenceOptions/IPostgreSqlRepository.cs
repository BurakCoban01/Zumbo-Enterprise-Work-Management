using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Persistence.PostgreSql;

public interface IPostgreSqlRepository<TDocument> : IDocumentRepository<TDocument>
    where TDocument : class, IDocument;
