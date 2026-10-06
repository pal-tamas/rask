// A whole page, written in React. ReactReport.cs carries the [Route]; nothing here knows that.
//
// `data-rask-nav` makes the link an in-app navigation, so pressing it swaps the page without reloading
// the document. The button does the same from code, with the app's C# routes: `@rask/routes` is
// generated from the [Route] pages, so a page that is renamed or gains a parameter stops this compiling.
import type { ReactReportProps } from '@rask/ReactReport.props'
import { Routes } from '@rask/routes'

export default function ReactReport({ back }: ReactReportProps) {
  return (
    <article data-testid="react-report">
      <h1 className="text-3xl font-bold mb-1">An island as a whole page</h1>

      <p className="text-ui-muted">
        React rendered everything below the site's own chrome. The title in the tab came from C#, and a
        skeleton stood here until this chunk loaded.
      </p>

      <a href={back} data-rask-nav="" data-testid="react-report-back" className="underline">
        Back to the islands
      </a>

      <button
        type="button"
        data-testid="react-report-routes"
        className="underline ms-4"
        onClick={() => Routes.RoutingAboutPage().Go()}
      >
        Go to a C# route
      </button>
    </article>
  )
}
