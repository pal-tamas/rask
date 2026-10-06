// A whole page, written in React. ReactReport.cs carries the [Route]; nothing here knows that.
//
// `data-rask-nav` is the one Rask-specific thing in the file: it makes the link an in-app navigation,
// so pressing it swaps the page without reloading the document.
import type { ReactReportProps } from '@rask/ReactReport.props'

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
    </article>
  )
}
