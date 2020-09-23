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

        public void SetData(string json)
        {
            var data = SimpleJSON.JSON.Parse(json);
            if (data["uid"] != uid)
                return;

            if (!string.IsNullOrEmpty(data["pos"]))
            {
                var p = data["pos"].ToString().Replace("(", "").Replace(")", "").Split(',');
                Vector3 pos = new Vector3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2]));
                transform.position = pos;
            }

            if (!string.IsNullOrEmpty(data["rot"]))
            {
                var r = data["rot"].ToString().Replace("(", "").Replace(")", "").Split(',');
                Quaternion rot = new Quaternion(float.Parse(r[0]), float.Parse(r[1]), float.Parse(r[2]), float.Parse(r[3]));
                transform.rotation = rot;
            }
        }

        public string GetData()
        {
            string pos = "";
            string rot = "";

            if (syncPosition)
                pos = transform.position.ToString();

            if (syncRotation)
                rot = transform.rotation.ToString();

            var data = new {name = m_name,uid = m_uid,pos,rot};
            var json = JsonUtility.ToJson(data);
            return json;
        }

    }
}
