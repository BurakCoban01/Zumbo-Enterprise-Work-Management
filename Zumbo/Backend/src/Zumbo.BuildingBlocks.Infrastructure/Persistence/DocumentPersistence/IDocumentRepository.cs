using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.BuildingBlocks.Infrastructure.Persistence;

public interface IDocumentRepository<TDocument> : ApplicationPersistence.IDocumentRepository<TDocument>
    where TDocument : class, ApplicationPersistence.IDocument;
