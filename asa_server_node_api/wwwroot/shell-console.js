window.shellConsole = {
  registerAutocomplete(element, dotNetRef) {
    if (!element || !dotNetRef) {
      return;
    }

    if (element._shellAutocompleteHandler) {
      element.removeEventListener("keydown", element._shellAutocompleteHandler);
    }

    const handler = async (event) => {
      if (event.key !== "Tab") {
        return;
      }

      event.preventDefault();
      await dotNetRef.invokeMethodAsync("HandleTabAutocomplete");
    };

    element.addEventListener("keydown", handler);
    element._shellAutocompleteHandler = handler;
  },

  scrollToBottom(element) {
    if (!element) {
      return;
    }

    const scroll = () => {
      element.scrollTo({
        top: element.scrollHeight,
        behavior: "smooth"
      });
    };

    requestAnimationFrame(() => {
      scroll();

      requestAnimationFrame(() => {
        scroll();
      });
    });

    setTimeout(scroll, 60);
  }
};
