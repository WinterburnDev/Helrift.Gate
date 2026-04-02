// App/Repositories/ITownProjectConfigRepository.cs
using Helrift.Gate.Contracts.TownProjects;

namespace Helrift.Gate.App.Repositories;

/// <summary>
/// Repository for loading Town Project configuration from Firebase.
/// </summary>
public interface ITownProjectConfigRepository : IVersionedConfigRepository<TownProjectConfigRoot, RealmProjectConfigRef>
{
}