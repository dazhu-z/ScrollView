using System;
using System.Collections;
using System.Collections.Generic;
using AillieoUtils;
using UnityEngine;

public class ScrollViewExItem : MonoBehaviour
{
    public ScrollViewEx scrollView;
    
    public int itemIndex;
    public bool isSelected;

    public void SetSelected(bool value)
    {
        isSelected = value;
        OnSelected();
    }
    
    //选择监听方法
    public virtual void OnSelected()
    {
        
    }
    
    //点击监听方法
    public virtual void OnClick()
    {
        scrollView.onClickItem.Invoke(itemIndex, this);
    }
}
