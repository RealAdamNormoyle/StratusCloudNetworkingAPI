using Newtonsoft.Json;
using SimpleJSON;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StratusCloudNetworking
{
    public class NetworkObject : MonoBehaviour
    {
        string m_uid;
        string m_name;
        bool m_isLocalObject;

        public string uid { get { return m_uid; } }
        public bool isLocalObject { get { return m_isLocalObject; } }
        public bool syncPosition;
        public bool syncRotation;
        public bool syncData;

        Dictionary<string, object> syncedData = new Dictionary<string, object>();
        Vector3 m_position;
        Quaternion m_rotation;

        public void LateUpdate()
        {
            if (isLocalObject)
            {
                m_position = transform.position;
                m_rotation = transform.rotation;
            }
            else
            {
                transform.position = Vector3.Lerp(transform.position,m_position,Time.deltaTime);
                transform.rotation = Quaternion.Lerp(transform.rotation, m_rotation, Time.deltaTime); ;
            }
        }

        public string CreateLocalObject(string name)
        {
            m_name = name;
            m_uid = Guid.NewGuid().ToString();
            m_isLocalObject = true;
            return m_uid;
        }

        public void SetRemoteObject(string s,string name)
        {
            m_name = name;
            m_isLocalObject = false;
            m_uid = s;
        }

        public object GetSyncedData(string key)
        {
            if (syncedData.ContainsKey(key))
                return syncedData[key];

            return null;
        }

        public void SetSyncedData(string key,object obj)
        {
            if (!isLocalObject)
                return;

            if (!syncedData.ContainsKey(key))
                syncedData.Add(key, obj);

            syncedData[key] = obj;
        }

        public virtual void OnNetworkUpdate() { }

        public virtual void BeforeNetworkUpdate() { }

        public void SetData(ObjectData data)
        {
            if (isLocalObject)
                return;

            if (data.uid != uid)
                return;

            if (data.pos)
                m_position = data.position.ToVector3();
            
            if (data.rot)
                m_rotation = data.rotation.ToQuaternion();
            
            syncedData = data.GetSyncData();
            OnNetworkUpdate();
        }

        public ObjectData GetData()
        {
            try
            {
                BeforeNetworkUpdate();
                ObjectData data = new ObjectData();
                data.name = m_name;
                data.uid = m_uid;
                data.position = new V3(m_position);
                data.rotation = new V4(m_rotation);
                data.pos = syncPosition;
                data.rot = syncRotation;
                if(syncData)
                    data.SetSyncData(syncedData);

                return data;
            }
            catch (Exception e )
            {
                Debug.LogWarning(e);
            }

            return null;
        }

    }

    [System.Serializable]
    public class ObjectData
    {
        public string name;
        public string uid;
        public bool pos;
        public bool rot;
        public V3 position;
        public V4 rotation;
        public List<string> dataStrings;
        Dictionary<string, int> ints = new Dictionary<string, int>();
        Dictionary<string, string> strings = new Dictionary<string, string>();
        Dictionary<string, bool> bools = new Dictionary<string, bool>();
        Dictionary<string, float> floats = new Dictionary<string, float>();
        Dictionary<string, object> syncedData = new Dictionary<string, object>();

        public static ObjectData FromJson(JSONObject objData)
        {
            ObjectData obj = new ObjectData();
            obj.name = objData["name"];
            obj.pos = objData["pos"].AsBool;
            var s = objData["position"].AsObject;
            obj.position = new V3(s["x"].AsFloat,s["y"].AsFloat, s["z"].AsFloat);
            obj.rot = objData["rot"].AsBool;
            s = objData["rotation"].AsObject;
            obj.rotation = new V4(s["x"].AsFloat, s["y"].AsFloat, s["z"].AsFloat, s["w"].AsFloat);
            obj.uid = objData["uid"];
            foreach (var item in objData["dataStrings"].AsArray)
            {
                switch (item.Value["t"].AsInt)
                {
                    default:
                        break;
                    case 0:
                        obj.ints.Add(item.Value["k"], item.Value["o"].AsInt);
                        obj.syncedData.Add(item.Value["k"], (object)(item.Value["o"].AsInt));
                        break;
                    case 1:
                        obj.floats.Add(item.Value["k"], item.Value["o"].AsFloat);
                        obj.syncedData.Add(item.Value["k"], (object)(item.Value["o"].AsFloat));
                        break;
                    case 2:
                        obj.syncedData.Add(item.Value["k"], (object)(item.Value["o"]));
                        obj.strings.Add(item.Value["k"], item.Value["o"]);
                        break;
                    case 3:
                        obj.syncedData.Add(item.Value["k"], (object)(item.Value["o"].AsBool));
                        obj.bools.Add(item.Value["k"], item.Value["o"].AsBool);
                        break;
                }
            }
            return obj;
        }

        internal Dictionary<string, object> GetSyncData()
        {
            return syncedData;
        }

        internal void SetSyncData(Dictionary<string, object> d)
        {
            ints.Clear();
            strings.Clear();
            floats.Clear();
            bools.Clear();

            foreach (var item in d)
            {
                Debug.Log(item.Key);

                if(item.Value.GetType() == typeof(int))
                    ints.Add(item.Key, (int)item.Value);
                else if (item.Value.GetType() == typeof(string))
                    strings.Add(item.Key, (string)item.Value);
                else if (item.Value.GetType() == typeof(float))
                    floats.Add(item.Key, (float)item.Value);
                else if (item.Value.GetType() == typeof(bool))
                    bools.Add(item.Key, (bool)item.Value);
                
            }

            dataStrings = new List<string>();
            foreach (var item in ints)
            {
                var da = JsonConvert.SerializeObject((new DynamicObject(DynamicObject.ObjectType.INT, item.Key, item.Value)).Pack());
                Console.WriteLine(da);
                dataStrings.Add(da);
            }
            foreach (var item in strings)
            {
                var da = JsonConvert.SerializeObject((new DynamicObject(DynamicObject.ObjectType.STRING, item.Key, item.Value)).Pack());
                Console.WriteLine(da);

                dataStrings.Add(da);
            }
            foreach (var item in bools)
            {
                var da = JsonConvert.SerializeObject((new DynamicObject(DynamicObject.ObjectType.BOOL, item.Key, item.Value)).Pack());
                dataStrings.Add(da);
            }
            foreach (var item in floats)
            {
                var da = JsonConvert.SerializeObject((new DynamicObject(DynamicObject.ObjectType.FLOAT, item.Key, item.Value)).Pack());
                dataStrings.Add(da);
            }

            Console.WriteLine(dataStrings.Count);

        }
    }

    [System.Serializable]
    public class DynamicObject
    {
        public enum ObjectType {
            NULL = -1,
            INT,
            FLOAT,
            STRING,
            BOOL
        }

        public int objectType;
        public string key;
        public object obj;

        public DynamicObject(ObjectType type,string k,object o)
        {
            objectType = (int)type;
            key = k;
            obj = o;
        }

        public object Pack()
        {
            switch ((ObjectType)objectType)
            {
                case ObjectType.STRING:
                    return new { t = objectType, k = key, o = (string)obj };

                case ObjectType.INT:
                    return new { t = objectType, k = key, o = (int)obj };

                case ObjectType.FLOAT:
                    return new { t = objectType, k = key, o = (float)obj };

                case ObjectType.BOOL:
                    return new { t = objectType, k = key, o = (bool)obj };

            }

            return new { t = -1};
        }
    }
}
