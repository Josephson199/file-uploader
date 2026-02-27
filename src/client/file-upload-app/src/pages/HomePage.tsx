import React from 'react'

const HomePage: React.FC = () => {
  return (
    <div>
      <h1>Welcome to the Home Page!</h1>
      {true ? (
        <div>
          <p>Hello, authenticated user!</p>
        </div>
      ) : (
        <div>
          <p>Please log in to access your personalized content.</p>
        </div>
      )}
    </div>
  )
}

export default HomePage