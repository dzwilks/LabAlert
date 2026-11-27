import { useCallback, useEffect, useState } from 'react'
import './App.css'

function App() {
  const [patient, setPatient] = useState('')
  const [unit, setUnit] = useState('')
  const [results, setResults] = useState([])
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [pending, setPending] = useState(null)

  const load = useCallback(async (signal) => {
    const query = new URLSearchParams({ patient, unit })
    const response = await fetch(`/api/results?${query}`, { signal })
    if (!response.ok) throw new Error('Unable to load results.')
    return response.json()
  }, [patient, unit])

  useEffect(() => {
    const controller = new AbortController()
    load(controller.signal)
      .then(data => { setResults(data); setError('') })
      .catch(err => { if (err.name !== 'AbortError') setError('Unable to load results.') })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [load])

  async function acknowledge(id) {
    setPending(id)
    let acknowledged = false
    try {
      const response = await fetch(`/api/results/${id}/acknowledge`, { method: 'POST' })
      if (!response.ok) throw new Error()
      acknowledged = true
      setResults(current => current.filter(result => result.id !== id))
      setResults(await load())
      setError('')
    } catch {
      setError(acknowledged ? 'Result acknowledged, but refresh failed. Reload the page.' : 'Unable to acknowledge result. Please try again.')
    } finally {
      setPending(null)
    }
  }

  return (
    <main>
      <h1>LabAlert</h1>
      <p>Unacknowledged abnormal and critical results</p>
      <div className="filters">
        <label>Patient name or ID <input disabled={pending !== null} value={patient} onChange={e => { setLoading(true); setPatient(e.target.value) }} /></label>
        <label>Patient unit <input disabled={pending !== null} value={unit} onChange={e => { setLoading(true); setUnit(e.target.value) }} /></label>
      </div>
      {error && <p role="alert">{error}</p>}
      {loading ? <p>Loading results…</p> : results.length === 0 ? !error && <p>No flagged results found.</p> : (
        <div className="table-wrap">
          <table>
            <thead><tr><th>Priority</th><th>Patient</th><th>Unit</th><th>Order</th><th>Result</th><th>Reference</th><th>Flag</th><th>Received (UTC)</th><th>Action</th></tr></thead>
            <tbody>{results.map(result => (
              <tr key={result.id}>
                <td>{result.severity}</td>
                <td>{result.patientName}<br />{result.patientId}</td>
                <td>{result.unit}</td>
                <td>{result.orderName}<br />{result.orderId}</td>
                <td>{result.testName || result.testCode}<br />{result.value} {result.units}</td>
                <td>{result.referenceRange}</td>
                <td>{result.abnormalFlag}</td>
                <td>{result.receivedAt.replace('T', ' ')}</td>
                <td><button disabled={pending !== null} onClick={() => acknowledge(result.id)}>Acknowledge</button></td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      )}
    </main>
  )
}

export default App
