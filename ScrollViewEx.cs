using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.Events;

namespace AillieoUtils
{
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class ScrollViewEx : ScrollView
    {
        
        [SerializeField]
        private int m_pageSize = 50;

        public int pageSize => m_pageSize;

        private int startOffset = 0;

        private Func<int> realItemCountFunc;

        private bool canNextPage = false;
        
        
        public class ScrollItemWithRect
        {
            // scroll item 身上的 RectTransform组件
            public RectTransform item;

            // scroll item 在scrollview中的位置
            public Rect rect;

            // rect 是否需要更新
            public bool rectDirty = true;
        }

        int m_dataCount = 0;
        List<ScrollItemWithRect> managedItems = new List<ScrollItemWithRect>();

        // for hide and show
        public enum ItemLayoutType
        {
                                            // 最后一位表示滚动方向
            Vertical = 1,                   // 0001
            Horizontal = 2,                 // 0010
            VerticalThenHorizontal = 4,     // 0100
            HorizontalThenVertical = 5,     // 0101
        }
        public const int flagScrollDirection = 1;  // 0001


        [SerializeField]
        ItemLayoutType m_layoutType = ItemLayoutType.Vertical;
        protected ItemLayoutType layoutType { get { return m_layoutType; } }


        // const int 代替 enum 减少 (int)和(CriticalItemType)转换
        protected static class CriticalItemType
        {
            public const int UpToHide = 0;
            public const int DownToHide = 1;
            public const int UpToShow = 2;
            public const int DownToShow = 3;
        }
        // 只保存4个临界index
        protected int[] criticalItemIndex = new int[4];
        Rect refRect;

        // resource management
        SimpleObjPool<RectTransform> itemPool = null;

        [Tooltip("初始化时池内item数量")]
        public int poolSize;

        [Tooltip("默认item尺寸")]
        public Vector2 defaultItemSize;

        [Tooltip("默认item间隔")]
        public Vector2 defaultItemSpace;

        //设置尺寸回调
        public Func<int, Vector2> itemSizeFunc;
        
        public Func<int, RectTransform> itemGetFunc;
        public Action<RectTransform> itemRecycleFunc;
        public Action<RectTransform> RecycleFunc;
        private Action UpdateCriticalItemsPreprocess = null;
        //选择元素回调
        private Action<int, RectTransform> selectIndexFunc;
        private UnityEvent<int, ScrollViewExItem> _onClickItem;
        
        // status
        private bool initialized = false;
        private int willUpdateData = 0;

        public override void SetUpdateFunc(Action<int,RectTransform> func)
        {
            if (func != null)
            {
                var f = func;
                func = (index, rect) =>
                {
                    f(index + startOffset, rect);
                };
            }
            base.SetUpdateFunc(func);
        }

        public void SetItemSizeFunc(Func<int, Vector2> func)
        {
            if (func != null)
            {
                var f = func;
                func = (index) =>
                {
                    return f(index + startOffset);
                };
            }
            itemSizeFunc = func;
        }

        public override void SetItemCountFunc(Func<int> func)
        {
            realItemCountFunc = func;
            if (func != null)
            {
                var f = func;
                func = () => Mathf.Min(f(), pageSize);
            }
            base.SetItemCountFunc(func);
        }
        public void SetItemRecycleFunc(Action<RectTransform> func)
        {
            RecycleFunc = func;
        }
        public void SetSelectIndexFunc(Action<int,RectTransform> func)
        {
            selectIndexFunc = func;
        }
        
        public void SetUpdateCriticalItemsPreprocess(Action func)
        {
            UpdateCriticalItemsPreprocess = func;
        }

        public void SetItemGetAndRecycleFunc(Func<int, RectTransform> getFunc, Action<RectTransform> recycleFunc)
        {
            if(getFunc != null && recycleFunc != null)
            {
                itemGetFunc = getFunc;
                itemRecycleFunc = recycleFunc;
            }
        }

        public void UpdateData(bool immediately = true)
        {
            if (!initialized)
            {
                InitScrollView();
            }
            if(immediately)
            {
                willUpdateData |= 3; // 0011
                InternalUpdateData();
            }
            else
            {
                if(willUpdateData == 0 && gameObject.active)
                {
                    StartCoroutine(DelayUpdateData());
                }
                willUpdateData |= 3;
            }
        }

        public void UpdateDataIncrementally(bool immediately = true)
        {
            if (!initialized)
            {
                InitScrollView();
            }
            if (immediately)
            {
                willUpdateData |= 1; // 0001
                InternalUpdateData();
            }
            else
            {
                if (willUpdateData == 0)
                {
                    StartCoroutine(DelayUpdateData());
                }
                willUpdateData |= 1;
            }
        }

        public void ScrollTo(int index)
        {
            InternalScrollTo(index);
        }

        protected void InternalScrollTo(int index)
        {
            int count = 0;
            if (realItemCountFunc != null)
            {
                count = realItemCountFunc();
            }
            index = Mathf.Clamp(index, 0, count - 1);
            startOffset = Mathf.Clamp(index - pageSize / 2, 0, count - itemCountFunc());
            UpdateData(true);
            
            index = Mathf.Clamp(index, 0, m_dataCount - 1);
            EnsureItemRect(index);
            Rect r = managedItems[index].rect;
            int dir = (int)layoutType & flagScrollDirection;
            if (dir == 1)
            {
                // vertical
                float value = 1 - (-r.yMax / (content.sizeDelta.y - refRect.height));
                //value = Mathf.Clamp01(value);
                SetNormalizedPosition(value, 1);
            }
            else
            {
                // horizontal
                float value = r.xMin / (content.sizeDelta.x - refRect.width);
                //value = Mathf.Clamp01(value);
                SetNormalizedPosition(value, 0);
            }
        }

        private IEnumerator DelayUpdateData()
        {
            yield return null;
            InternalUpdateData();
        }


        protected override void InternalUpdateData()
        {
            int newDataCount = 0;
            bool keepOldItems = ((willUpdateData & 2) == 0);

            if (itemCountFunc != null)
            {
                newDataCount = itemCountFunc();
            }

            // if (newDataCount != managedItems.Count)
            if (true)
            {
                if (managedItems.Count < newDataCount) //增加
                {
                    if(!keepOldItems)
                    {
                        foreach (var itemWithRect in managedItems)
                        {
                            // 重置所有rect
                            itemWithRect.rectDirty = true;
                        }
                    }

                    while (managedItems.Count < newDataCount)
                    {
                        managedItems.Add(new ScrollItemWithRect());
                    }
                }
                else //减少 保留空位 避免GC
                {
                    for (int i = 0, count = managedItems.Count; i < count; ++i)
                    {
                        if(i < newDataCount)
                        {
                            // 重置所有rect
                            if(!keepOldItems)
                            {
                                managedItems[i].rectDirty = true;
                            }

                            if(i == newDataCount - 1)
                            {
                                managedItems[i].rectDirty = true;
                            }
                        }

                        // 超出部分 清理回收item
                        if (i >= newDataCount)
                        {
                            managedItems[i].rectDirty = true;
                            if (managedItems[i].item != null)
                            {
                                RecycleOldItem(managedItems[i].item);
                                managedItems[i].item = null;
                            }
                        }
                    }
                }
            }
            else
            {
                if(!keepOldItems)
                {
                    for (int i = 0, count = managedItems.Count; i < count; ++i)
                    {
                        // 重置所有rect
                        managedItems[i].rectDirty = true;
                    }
                }
            }

            m_dataCount = newDataCount;

            ResetCriticalItems();

            willUpdateData = 0;
        }

        void ResetCriticalItems()
        {
            bool hasItem, shouldShow;
            int firstIndex = -1, lastIndex = -1;

            for (int i = 0; i < m_dataCount; i++)
            {
                hasItem = managedItems[i].item != null;
                shouldShow = ShouldItemSeenAtIndex(i);

                if (shouldShow)
                {
                    if (firstIndex == -1)
                    {
                        firstIndex = i;
                    }
                    lastIndex = i;
                }

                if (hasItem && shouldShow)
                {
                    // 应显示且已显示
                    SetDataForItemAtIndex(managedItems[i].item, i);
                    continue;
                }

                if (hasItem == shouldShow)
                {
                    // 不应显示且未显示
                    //if (firstIndex != -1)
                    //{
                    //    // 已经遍历完所有要显示的了 后边的先跳过
                    //    break;
                    //}
                    continue;
                }

                if (hasItem && !shouldShow)
                {
                    // 不该显示 但是有
                    RecycleOldItem(managedItems[i].item);
                    managedItems[i].item = null;
                    continue;
                }

                if (shouldShow && !hasItem)
                {
                    // 需要显示 但是没有
                    RectTransform item = GetNewItem(i);
                    managedItems[i].item = item;
                    OnGetItemForDataIndex(item, i);
                    continue;
                }

            }

            // content.localPosition = Vector2.zero;
            criticalItemIndex[CriticalItemType.UpToHide] = firstIndex;
            criticalItemIndex[CriticalItemType.DownToHide] = lastIndex;
            criticalItemIndex[CriticalItemType.UpToShow] = Mathf.Max(firstIndex - 1, 0);
            criticalItemIndex[CriticalItemType.DownToShow] = Mathf.Min(lastIndex + 1, m_dataCount - 1);

        }

        protected override void SetContentAnchoredPosition(Vector2 position)
        {
            base.SetContentAnchoredPosition(position);
            UpdateCriticalItemsPreprocess?.Invoke();
            UpdateCriticalItems();
        }

        protected override void SetNormalizedPosition(float value, int axis)
        {
            base.SetNormalizedPosition(value, axis);
            ResetCriticalItems();
        }

        RectTransform GetCriticalItem(int type)
        {
            int index = criticalItemIndex[type];
            if(index >= 0 && index < m_dataCount)
            {
                return managedItems[index].item;
            }
            return null;
        }
        void UpdateCriticalItems()
        {
            //if (itemSizeFunc != null)
            //{
            //    managedItems.ForEach(item =>
            //    {
            //        item.rectDirty = true;
            //    });
            //}
  
            bool dirty = true;

            while (dirty)
            {
                dirty = false;

                for (int i = CriticalItemType.UpToHide; i <= CriticalItemType.DownToShow; i ++)
                {
                    if(i <= CriticalItemType.DownToHide) //隐藏离开可见区域的item
                    {
                        dirty = dirty || CheckAndHideItem(i);
                    }
                    else  //显示进入可见区域的item
                    {
                        dirty = dirty || CheckAndShowItem(i);
                    }
                }
            }
        }

        public void ForceUpdateCriticalItems()
        {
            // Debug.Log("count : "+managedItems.Count);
            //
            // managedItems.ForEach(item =>
            // {
            //     item.rectDirty = true;
            // });
            //
            UpdateCriticalItems();
        }

        private bool CheckAndHideItem(int criticalItemType)
        {
            RectTransform item = GetCriticalItem(criticalItemType);
            int criticalIndex = criticalItemIndex[criticalItemType];
            if (item != null && !ShouldItemSeenAtIndex(criticalIndex))
            {
                RecycleOldItem(item);
                managedItems[criticalIndex].item = null;
                //Debug.Log("回收了 " + criticalIndex);

                if (criticalItemType == CriticalItemType.UpToHide)
                {
                    // 最上隐藏了一个
                    criticalItemIndex[criticalItemType + 2] = Mathf.Max(criticalIndex, criticalItemIndex[criticalItemType + 2]);
                    criticalItemIndex[criticalItemType]++;
                }
                else
                {
                    // 最下隐藏了一个
                    criticalItemIndex[criticalItemType + 2] = Mathf.Min(criticalIndex, criticalItemIndex[criticalItemType + 2]);
                    criticalItemIndex[criticalItemType]--;
                }
                criticalItemIndex[criticalItemType] = Mathf.Clamp(criticalItemIndex[criticalItemType], 0, m_dataCount - 1);
                return true;
            }
            
            return false;
        }


        private bool CheckAndShowItem(int criticalItemType)
        {
            RectTransform item = GetCriticalItem(criticalItemType);
            int criticalIndex = criticalItemIndex[criticalItemType];
            //if (item == null && ShouldItemFullySeenAtIndex(criticalItemIndex[criticalItemType - 2]))

            if (item == null && ShouldItemSeenAtIndex(criticalIndex))
            {
                RectTransform newItem = GetNewItem(criticalIndex);
                OnGetItemForDataIndex(newItem, criticalIndex);
                //Debug.Log("创建了 " + criticalIndex);
                managedItems[criticalIndex].item = newItem;


                if (criticalItemType == CriticalItemType.UpToShow)
                {
                    // 最上显示了一个
                    criticalItemIndex[criticalItemType - 2] = Mathf.Min(criticalIndex, criticalItemIndex[criticalItemType - 2]);
                    criticalItemIndex[criticalItemType]--;
                }
                else
                {
                    // 最下显示了一个
                    criticalItemIndex[criticalItemType - 2] = Mathf.Max(criticalIndex, criticalItemIndex[criticalItemType - 2]);
                    criticalItemIndex[criticalItemType]++;
                }
                criticalItemIndex[criticalItemType] = Mathf.Clamp(criticalItemIndex[criticalItemType], 0, m_dataCount - 1);
                return true;
            }
            return false;
        }
        
        bool ShouldItemSeenAtIndex(int index)
        {
            if(index < 0 || index >= m_dataCount)
            {
                return false;
            }
            EnsureItemRect(index);
            return new Rect(refRect.position - content.anchoredPosition, refRect.size).Overlaps(managedItems[index].rect);
        }

        bool ShouldItemFullySeenAtIndex(int index)
        {
            if (index < 0 || index >= m_dataCount)
            {
                return false;
            }
            EnsureItemRect(index);
            return IsRectContains(new Rect(refRect.position - content.anchoredPosition, refRect.size),(managedItems[index].rect));
        }

        bool IsRectContains(Rect outRect, Rect inRect, bool bothDimensions = false)
        {

            if (bothDimensions)
            {
                bool xContains = (outRect.xMax >= inRect.xMax) && (outRect.xMin <= inRect.xMin);
                bool yContains = (outRect.yMax >= inRect.yMax) && (outRect.yMin <= inRect.yMin);
                return xContains && yContains;
            }
            else
            {
                int dir = (int)layoutType & flagScrollDirection;
                if(dir == 1)
                {
                    // 垂直滚动 只计算y向
                    return (outRect.yMax >= inRect.yMax) && (outRect.yMin <= inRect.yMin);
                }
                else // = 0
                {
                    // 水平滚动 只计算x向
                    return (outRect.xMax >= inRect.xMax) && (outRect.xMin <= inRect.xMin);
                }
            }
        }


        void InitPool()
        {
            GameObject poolNode = new GameObject("POOL");
            poolNode.SetActive(false);
            poolNode.transform.SetParent(transform,false);
            itemPool = new SimpleObjPool<RectTransform>(
                poolSize,
                (RectTransform item) => {
                    // 回收
                    item.transform.SetParent(poolNode.transform,false);
                },
                () => {
                    // 构造
                    GameObject itemObj = Instantiate(itemTemplate.gameObject);
                    
                    //设置元素的滚动视图组件(即this)
                    if (itemObj.GetComponent<ScrollViewExItem>())
                    {
                        itemObj.GetComponent<ScrollViewExItem>().scrollView = this;
                    }
                    
                    RectTransform item = itemObj.GetComponent<RectTransform>();
                    itemObj.transform.SetParent(poolNode.transform,false);

                    item.anchorMin = Vector2.up;
                    item.anchorMax = Vector2.up;
                    item.pivot = Vector2.zero;
                    //rectTrans.pivot = Vector2.up;

                    itemObj.SetActive(true);
                    return item;
                });
        }

        void OnGetItemForDataIndex(RectTransform item, int index)
        {
            SetDataForItemAtIndex(item, index);
            item.transform.SetParent(content, false);
        }


        void SetDataForItemAtIndex(RectTransform item, int index)
        {
            if (updateFunc != null)
                updateFunc(index,item);

            SetPosForItemAtIndex(item,index);
        }


        void SetPosForItemAtIndex(RectTransform item, int index)
        {
            EnsureItemRect(index);
            var managedItem = managedItems[index];
            if (managedItem.item != null && managedItem.item.GetComponent<ScrollViewExItem>())
            {
                item.GetComponent<ScrollViewExItem>().itemIndex = index;
            }
            Rect r = managedItem.rect;
            item.localPosition = r.position;
            item.sizeDelta = r.size;
        }


        Vector2 GetItemSize(int index,ScrollItemWithRect item)
        {
            if(index >= 0 && index <= m_dataCount)
            {
                if (itemSizeFunc != null)
                {
                    return itemSizeFunc(index);
                }
            }
            return defaultItemSize;
        }

        private RectTransform GetNewItem(int index)
        {
            RectTransform item;
            if(itemGetFunc != null)
            {
                item = itemGetFunc(index);
            }
            else
            {
                item = itemPool.Get();
            }
            return item;
        }

        private void RecycleOldItem(RectTransform item)
        {
            if (itemRecycleFunc != null)
            {
                itemRecycleFunc(item);
            }
            else
            {
                itemPool.Recycle(item);
            }
            if (RecycleFunc != null)
            {
                RecycleFunc(item);
            }
        }

        void InitScrollView()
        {
            initialized = true;

            // 根据设置来控制原ScrollRect的滚动方向
            int dir = (int)layoutType & flagScrollDirection;

            content.pivot = Vector2.up;
            InitPool();
            UpdateRefRect();
        }


        Vector3[] viewWorldConers = new Vector3[4];
        Vector3[] rectCorners = new Vector3[2];
        void UpdateRefRect()
        {
            /*
             *  WorldCorners
             * 
             *    1 ------- 2     
             *    |         |
             *    |         |
             *    0 ------- 3
             * 
             */

            // refRect是在Content节点下的 viewport的 rect
            viewRect.GetWorldCorners(viewWorldConers);
            rectCorners[0] = content.transform.InverseTransformPoint(viewWorldConers[0]);
            rectCorners[1] = content.transform.InverseTransformPoint(viewWorldConers[2]);
            refRect = new Rect((Vector2)rectCorners[0] - content.anchoredPosition, rectCorners[1] - rectCorners[0]);
        }

        void MovePos(ref Vector2 pos, Vector2 size)
        {
            // 注意 所有的rect都是左下角为基准
            switch (layoutType)
            {
                case ItemLayoutType.Vertical:
                    // 垂直方向 向下移动
                    pos.y -= size.y;
                    break;
                case ItemLayoutType.Horizontal:
                    // 水平方向 向右移动
                    pos.x += size.x;
                    break;
                case ItemLayoutType.VerticalThenHorizontal:
                    pos.y -= size.y;
                    if (pos.y <= -(refRect.height - size.y / 2))
                    {
                        pos.y = 0;
                        pos.x += size.x;
                    }
                    break;
                case ItemLayoutType.HorizontalThenVertical:
                    pos.x += size.x;
                    if(pos.x >= refRect.width - size.x / 2)
                    {
                        pos.x = 0;
                        pos.y -= size.y;
                    }
                    break;
                default:
                    break;
            }
        }

        protected void EnsureItemRect(int index)
        {
            if (!managedItems[index].rectDirty)
            {
                // 已经是干净的了
                return;
            }

            ScrollItemWithRect firstItem = managedItems[0];
            if (firstItem.rectDirty)
            {
                Vector2 firstSize = GetItemSize(0, firstItem);
                firstItem.rect = CreateWithLeftTopAndSize(Vector2.zero, firstSize);
                firstItem.rect.position += defaultItemSpace;
                firstItem.rectDirty = false;
                if (firstItem.item)
                {
                    firstItem.item.localPosition = firstItem.rect.position;
                }
            }

            // 当前item之前的最近的已更新的rect
            int nearestClean = 0;
            for (int i = index; i >= 0; --i)
            {
                if (!managedItems[i].rectDirty)
                {
                    nearestClean = i;
                    break;
                }
            }

            // 需要更新 从 nearestClean 到 index 的尺寸
            Rect nearestCleanRect = managedItems[nearestClean].rect;
            Vector2 curPos = GetLeftTop(nearestCleanRect);
            Vector2 size = nearestCleanRect.size;
            MovePos(ref curPos, size);

            for (int i = nearestClean + 1; i <= index; i++)
            {
                size = GetItemSize(i, managedItems[i]);
                managedItems[i].rect = CreateWithLeftTopAndSize(curPos, size);
                managedItems[i].rect.position += defaultItemSpace;
                managedItems[i].rectDirty = false;
                MovePos(ref curPos, size);
                if (managedItems[i].item)
                {
                    managedItems[i].item.localPosition = managedItems[i].rect.position;
                }
            }

            Vector2 range = new Vector2(Mathf.Abs(curPos.x), Mathf.Abs(curPos.y));
            switch (layoutType)
            {
                case ItemLayoutType.VerticalThenHorizontal:
                    range.x += size.x;
                    range.y = refRect.height;
                    break;
                case ItemLayoutType.HorizontalThenVertical:
                    range.x = refRect.width;
                    if (curPos.x != 0)
                    {
                        range.y += size.y;
                    }

                    break;
                default:
                    break;
            }

            content.sizeDelta = range;
        }
        
        //选择Item
        public void SelectItem(int index)
        {
            for (int i = 0; i < managedItems.Count; i++)
            {
                var managedItem = managedItems[i];
                if (managedItem != null && managedItem.item != null && managedItem.item.GetComponent<ScrollViewExItem>())
                {
                    ScrollViewExItem item = managedItem.item.GetComponent<ScrollViewExItem>();
                    item.SetSelected(item.itemIndex == index);
                    if (item.itemIndex == index && selectIndexFunc != null)
                    {
                        selectIndexFunc(index, managedItem.item);
                    }
                }
            }
        }
        
        public UnityEvent<int, ScrollViewExItem> onClickItem => _onClickItem ?? (_onClickItem = new UnityEvent<int, ScrollViewExItem>());

        private static Vector2 GetLeftTop(Rect rect)
        {
            Vector2 ret = rect.position;
            ret.y += rect.size.y;
            return ret;
        }
        private static Rect CreateWithLeftTopAndSize(Vector2 leftTop, Vector2 size)
        {
            Vector2 leftBottom = leftTop - new Vector2(0,size.y);
            //Debug.Log(" leftBottom : "+leftBottom +" size : "+size );
            return new Rect(leftBottom,size);
        }


        protected override void OnDestroy()
        {
            if (itemPool != null)
            {
                itemPool.Purge();
            }
        }

        protected Rect GetItemLocalRect(int index)
        {
            if(index >= 0 && index < m_dataCount)
            {
                EnsureItemRect(index);
                return managedItems[index].rect;
            }
            return new Rect();
        }

        protected override void Awake()
        {
            base.Awake();
            onValueChanged.AddListener(OnValueChanged);
        }

        private void Update()
        {
            if (Input.GetMouseButtonUp(0) || Input.GetMouseButtonDown(0))
                canNextPage = true;
        }

        bool reloadFlag = false;


        private void OnValueChanged(Vector2 position)
        {
            if (reloadFlag)
            {
                UpdateData(true);
                reloadFlag = false;
            }
            if (Input.GetMouseButton(0) && !canNextPage) return;

            int toShow;
            int critical;
            bool downward;
            int pin;
            if (((int)layoutType & flagScrollDirection) == 1)
            {
                // 垂直滚动 只计算y向
                if (velocity.y > 0)
                {
                    // 向上
                    toShow = criticalItemIndex[CriticalItemType.DownToShow];
                    critical = pageSize - 1;
                    if (toShow < critical)
                    {
                        return;
                    }
                    pin = critical - 1;
                    downward = false;
                }
                else
                {
                    // 向下
                    toShow = criticalItemIndex[CriticalItemType.UpToShow];
                    critical = 0;
                    if (toShow > critical)
                    {
                        return;
                    }
                    pin = critical + 1;
                    downward = true;
                }
            }
            else // = 0
            {
                // 水平滚动 只计算x向
                if (velocity.x > 0)
                {
                    // 向右
                    toShow = criticalItemIndex[CriticalItemType.UpToShow];
                    critical = 0;
                    if (toShow > critical)
                    {
                        return;
                    }
                    pin = critical + 1;
                    downward = true;
                }
                else
                {
                    // 向左
                    toShow = criticalItemIndex[CriticalItemType.DownToShow];
                    critical = pageSize - 1;
                    if (toShow < critical)
                    {
                        return;
                    }
                    pin = critical - 1;
                    downward = false;
                }
            }

            // 翻页
            int old = startOffset;
            if (downward)
            {
                startOffset -= pageSize / 2;
            }
            else
            {
                startOffset += pageSize / 2;
            }
            canNextPage = false;


            int realDataCount = 0;
            if (realItemCountFunc != null)
            {
                realDataCount = realItemCountFunc();
            }
            startOffset = Mathf.Clamp(startOffset, 0, Mathf.Max(realDataCount - pageSize, 0));

            if (old != startOffset)
            {
                reloadFlag = true;

                // 计算 pin元素的世界坐标
                Rect rect = GetItemLocalRect(pin);
                Vector2 oldWorld = content.TransformPoint(rect.position);
                UpdateData(true);
                int dataCount = 0;
                if (itemCountFunc != null)
                {
                    dataCount = itemCountFunc();
                }
                if (dataCount > 0)
                {
                    EnsureItemRect(0);
                    if (dataCount > 1)
                    {
                        EnsureItemRect(dataCount - 1);
                    }
                }

                // 根据 pin元素的世界坐标 计算出content的position
                int pin2 = pin + old - startOffset;
                Rect rect2 = GetItemLocalRect(pin2);
                Vector2 newWorld = content.TransformPoint(rect2.position);
                Vector2 deltaWorld = newWorld - oldWorld;

                Vector2 deltaLocal = content.InverseTransformVector(deltaWorld);
                SetContentAnchoredPosition(content.anchoredPosition - deltaLocal);

                UpdateData(true);

                // 减速
                velocity /= 50f;
            }

        }
    }
}
