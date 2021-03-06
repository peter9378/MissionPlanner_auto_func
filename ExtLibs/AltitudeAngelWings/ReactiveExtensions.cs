using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace AltitudeAngelWings
{
    public static class ReactiveExtensions
    {
        public static IDisposable SubscribeAsync<T>(this IObservable<T> source, Func<T, Task> onNextAsync)
        {
            return source.Subscribe(i => onNextAsync(i).Wait());
        }

        public static IDisposable SubscribeVisualState<T>(this IObservable<T> source, FrameworkElement stateElement)
        {
            return source.ObserveOnDispatcher()
                         .Subscribe(state => VisualStateManager.GoToElementState(stateElement, state.ToString(), true));
        }
    }
}
