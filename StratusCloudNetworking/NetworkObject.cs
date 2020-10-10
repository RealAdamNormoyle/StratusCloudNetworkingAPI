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

        public void SetData(ObjectData data)
        {
            if (isLocalObject)
                return;


            if (data.uid != uid)
                return;

            if (!string.IsNullOrEmpty(data.position))
            {
                var p = data.position.ToString().Replace("(", "").Replace(")", "").Split(',');
                Vector3 pos = new Vector3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2]));
                m_position = pos;
            }

            if (!string.IsNullOrEmpty(data.rotation))
            {
                var r = data.rotation.ToString().Replace("(", "").Replace(")", "").Split(',');
                Quaternion rot = new Quaternion(float.Parse(r[0]), float.Parse(r[1]), float.Parse(r[2]), float.Parse(r[3]));
                m_rotation = rot;
            }
        }

        public string GetData()
        {
            string pos = "";
            string rot = "";
            string json = "";

            try
            {
                if (syncPosition)
                    pos = m_position.ToString();

                if (syncRotation)
                    rot = m_rotation.ToString();

                ObjectData data = new ObjectData();
                data.name = m_name;
                data.uid = m_uid;
                data.position = pos;
                data.rotation = rot;
                json = data.ToJson();
            }
            catch (Exception e )
            {
                Debug.LogWarning(e);
            }

            return json;
        }

    }

    [System.Serializable]
    public class ObjectData
    {
        public string name;
        public string uid;
        public string position;
        public string rotation;

        public string ToJson()
        {
            return JsonMapper.ToJson(this);
        }

        public static ObjectData FromJson(string json)
        {
            return JsonUtility.FromJson<ObjectData>(json);
        }
    }
}
