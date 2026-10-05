using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Directiva.CodeUI
{
    /// <summary>
    /// Modales y prompts runtime simples, construidos por código.
    /// </summary>
    public sealed class ModalLayer : VisualElement
    {
        private readonly LocalizationService _locale;
        private readonly VisualElement _overlay;
        private readonly VisualElement _card;
        private readonly Label _title;
        private readonly Label _message;
        private readonly VisualElement _content;
        private readonly VisualElement _actions;

        public ModalLayer(LocalizationService locale)
        {
            _locale = locale;

            AddToClassList("modal-layer");
            pickingMode = PickingMode.Ignore;

            _overlay = new VisualElement();
            _overlay.AddToClassList("modal-overlay");
            _overlay.style.display = DisplayStyle.None;
            _overlay.pickingMode = PickingMode.Position;

            _card = new VisualElement();
            _card.AddToClassList("modal-card");

            _title = new Label();
            _title.AddToClassList("modal-title");

            _message = new Label();
            _message.AddToClassList("modal-message");

            _content = new VisualElement();
            _content.AddToClassList("modal-content");

            _actions = new VisualElement();
            _actions.AddToClassList("modal-actions");

            _card.Add(_title);
            _card.Add(_message);
            _card.Add(_content);
            _card.Add(_actions);
            _overlay.Add(_card);
            Add(_overlay);

            _overlay.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.target == _overlay)
                    evt.StopPropagation();
            });
        }

        public void Confirm(
            string title,
            string message,
            Action onConfirm,
            string confirmText,
            string cancelText)
        {
            ShowBase(title, message);

            var cancel = new Button(Hide) { text = cancelText };
            var confirm = new Button(() =>
            {
                Hide();
                onConfirm?.Invoke();
            }) { text = confirmText };

            confirm.AddToClassList("danger-button");
            _actions.Add(cancel);
            _actions.Add(confirm);
            confirm.Focus();
        }

        public void Prompt(
            string title,
            string message,
            string initialValue,
            Action<string> onConfirm,
            string confirmText,
            string cancelText)
        {
            ShowBase(title, message);

            var field = new TextField();
            field.value = initialValue ?? string.Empty;
            field.AddToClassList("modal-input");
            _content.Add(field);

            Action submit = () =>
            {
                var value = field.value?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(value))
                    return;

                Hide();
                onConfirm?.Invoke(value);
            };

            var cancel = new Button(Hide) { text = cancelText };
            var confirm = new Button(() => submit()) { text = confirmText };

            _actions.Add(cancel);
            _actions.Add(confirm);

            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    Hide();
                    evt.StopImmediatePropagation();
                }
                else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    submit();
                    evt.StopImmediatePropagation();
                }
            });

            field.schedule.Execute(() =>
            {
                field.Focus();
                field.SelectAll();
            }).ExecuteLater(1);
        }

        public void Alert(string title, string message, string closeText)
        {
            ShowBase(title, message);
            var close = new Button(Hide) { text = closeText };
            _actions.Add(close);
            close.Focus();
        }

        private void ShowBase(string title, string message)
        {
            _title.text = title ?? string.Empty;
            _message.text = message ?? string.Empty;
            _content.Clear();
            _actions.Clear();
            _overlay.style.display = DisplayStyle.Flex;
            pickingMode = PickingMode.Position;
            BringToFront();
        }

        public void Hide()
        {
            _overlay.style.display = DisplayStyle.None;
            _content.Clear();
            _actions.Clear();
            pickingMode = PickingMode.Ignore;
        }
    }
}
