using System;
using System.Collections.Generic;

namespace DoodleDiplomacy.Gameplay
{
    public sealed class GameplayServiceRegistry
    {
        private readonly Dictionary<Type, object> _services = new();

        public void Register<T>(T service) where T : class
        {
            if (service == null)
            {
                _services.Remove(typeof(T));
                return;
            }

            _services[typeof(T)] = service;
        }

        public bool TryGet<T>(out T service) where T : class
        {
            if (_services.TryGetValue(typeof(T), out object value) && value is T typed)
            {
                service = typed;
                return true;
            }

            service = null;
            return false;
        }
    }
}
