using LDTK2GMS2Pipeline.LDTK;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.Sync;

internal partial class LDTK2GM
{
    /// <summary>
    /// Returns dictionary for LDTK -> GM entity instance identifier matches. Will generate a new id if Meta is missing
    /// </summary>
    private static Dictionary<string, string> InitializeEntityMatches(GMProject _gmProject, LDTKProject _ldtkProject)
    {
        Dictionary<string, string> result = new();

        foreach (var layer in _ldtkProject.levels.SelectMany(t => t.layerInstances))
        {
            foreach (LDTKProject.Level.EntityInstance instance in layer.entityInstances)
            {
                string id = instance.Meta?.identifier ?? GetRandomInstanceName(_gmProject);

                result.Add(instance.iid, id);
            }
        }

        return result;
    }

    /// <summary>
    /// Finds entity and object for given uid
    /// </summary>
    private static bool FindEntity(GMProject _gmProject, LDTKProject _ldtkProject, int _entityUid, out LDTKProject.Entity _entity, out GMObject _gmObject)
    {
        _entity = default!;
        _gmObject = default!;

        var entity = _ldtkProject.GetResource<LDTKProject.Entity>(_entityUid);
        if (entity == null)
            return false;

        var gmObjectName = entity.Meta?.identifier;
        if (gmObjectName == null)
            return false;

        var obj = _gmProject.FindResourceByName(gmObjectName, typeof(GMObject)) as GMObject;
        if (obj == null)
            return false;

        _gmObject = obj;
        _entity = entity;
        return true;
    }

    private static Random UniqueNameRandom = new Random();

    /// <summary>
    /// Generates a random new name for object instance to be used in GM
    /// </summary>
    private static string GetRandomInstanceName( GMProject _project )
    {
        while ( true )
        {
            string result = "inst_" + UniqueNameRandom.Next().ToString( "X" );
            if ( _project.FindResourceByName( result, typeof( GMRInstance ) ) == null )
                return result;
        }
    }
}