using UnityEngine;
using UnityEngine.UIElements;

namespace Chat
{
    public class ChatMessageView
    {
        private string _role;
        private string _content;
        private VisualElement _messageItem;
        private Label _roleLabel;
        private Label _contentLabel;
        private ScrollView _messagesList;

        public string Role
        {
            get => _role;
            set
            {
                _role = value;
                _roleLabel.text = _role;
            }
        }

        public string Content
        {
            get => _content;
            set
            {
                _content = value;
                _contentLabel.text = _content;
                _messagesList.ScrollTo(_messageItem);
            }
        }

        public ChatMessageView(string role, string content, VisualElement rootVisualElement)
        {
            _role = role;
            _content = "";
            _messagesList = rootVisualElement.Q<ScrollView>("MessagesList");

            CreateUIElement();
            AddToUI();

            Content = content;
        }

        private void CreateUIElement()
        {
            _messageItem = new VisualElement();
            _messageItem.AddToClassList("message-item");
            if (_role == "ユーザ")
            {
                _messageItem.AddToClassList("message-item--user");
            }
            else
            {
                _messageItem.AddToClassList("message-item--assistant");
            }

            _roleLabel = new Label(_role);
            _roleLabel.AddToClassList("user-name");

            _contentLabel = new Label("");
            _contentLabel.AddToClassList("message-content");

            _messageItem.Add(_roleLabel);
            _messageItem.Add(_contentLabel);
        }

        private void AddToUI()
        {
            if (_messagesList == null) return;
            _messagesList.Add(_messageItem);
            _messagesList.ScrollTo(_messageItem);
        }

        public VisualElement GetUIElement()
        {
            return _messageItem;
        }
    }
}