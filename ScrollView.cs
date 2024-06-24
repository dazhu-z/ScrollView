using System;
using UnityEngine;
using UnityEngine.UI;


namespace AillieoUtils
{
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class ScrollView : ScrollRect
    {

        [Tooltip("item的模板")]
        public RectTransform itemTemplate;
        
        //更新数据回调
        public Action<int, RectTransform> updateFunc;
        
        //设置数量回调（更新数据）
        public Func<int> itemCountFunc;

        public virtual void SetUpdateFunc(Action<int,RectTransform> func)
        {
            updateFunc = func;
        }

        public virtual void SetItemCountFunc(Func<int> func)
        {
            itemCountFunc = func;
            InternalUpdateData();
        }

        protected virtual void InternalUpdateData()
        {
            if (updateFunc == null)
            {
                return;
            }
            RemoveAllChildren();
            for (int i = 0; i < itemCountFunc(); i++)
            {
                GameObject itemObj = Instantiate(itemTemplate.gameObject, content, true);
                itemObj.transform.localPosition = itemTemplate.localPosition;
                itemObj.SetActive(true);
                updateFunc(i, itemObj.GetComponent<RectTransform>());
            }
        }

        public void RemoveAllChildren()
        {
            for(int i = 0;i < content.childCount; i++)
            {
                Transform child = content.GetChild(i);
                if (itemTemplate != child)
                {
                    Destroy(child.gameObject);
                }
            }
        }
    }

}